using Microsoft.AspNetCore.Authentication;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace FarmAPI.Machines.BambuLab
{
    public class BambuCloudCredentials(string email, string password, string? verificationCode = null)
    {
        public string Email { get; set; } = email;
        public string Password { get; set; } = password;
        public string? VerificationCode { get; set; } = verificationCode;
    }

    public static class BambuCloudUtilities
    {
        private static readonly HttpClient bambuCloutHttpClient = new()
        {
            BaseAddress = new Uri("https://api.bambulab.com")
        };

        public static async Task<bool> IsTokenAuthorized(string token)
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.bambulab.com/v1/user-service/my/messages");
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return (await bambuCloutHttpClient.SendAsync(requestMessage)).StatusCode != HttpStatusCode.Unauthorized;
        }

        public static async Task<string> UseToken(BambuCloudCredentials credentials)
        {
            HttpResponseMessage response;

            if (credentials.VerificationCode == null)
            {
                response = await bambuCloutHttpClient.PostAsJsonAsync("/v1/user-service/user/login", new
                {
                    account = credentials.Email,
                    credentials.Password
                });
            }
            else
            {
                response = await bambuCloutHttpClient.PostAsJsonAsync("/v1/user-service/user/login", new
                {
                    account = credentials.Email,
                    code = credentials.VerificationCode
                });
            }

            if (response.StatusCode == HttpStatusCode.BadRequest && credentials.VerificationCode != null)
            {
                throw new InvalidOperationException("Provided BambuCloud verification code may have expired!");
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await JsonDocument.ParseAsync(response.Content.ReadAsStream())
                ?? throw new NullReferenceException("Response body could not be parsed!");

            var accessToken = responseJson!.RootElement.GetProperty("accessToken").GetString()
                ?? throw new NullReferenceException("Response body does not contain an accessToken!");

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("BambuCloud access token was not provided on login response.");
            }

            if (accessToken == "verifyCode")
            {
                throw new InvalidOperationException("A two-factor email verification code must be provided to login into this BambuCloud account!");
            }

            await UseToken(accessToken, false);

            return accessToken;
        }

        public static async Task UseToken(string token, bool doValidate = true)
        {
            bambuCloutHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            if (doValidate && !(await IsTokenAuthorized(token)))
            {
                throw new InvalidOperationException("Cannot use an invalidated token!");
            }
        }

        public static async Task<MQTTCredentials> FetchMQTTCredentials(string token)
        {
            // Read the account ID from the accessToken payload.
            string[] JWTSections = token.Split('.');

            if (JWTSections.Length != 3)
            {
                // The access token Bambu Lab provided is NOT a JWT..? We need another way of getting our MQTT username aka (u_[some_number]).
                // https://github.com/greghesp/ha-bambulab/blob/1ebaf4cdc84aebf6d5802baf6899a1ab9c41b01a/custom_components/bambu_lab/pybambu/bambu_cloud.py#L273

                HttpResponseMessage res = await bambuCloutHttpClient.GetAsync("/v1/iot-service/api/user/project");
                res.EnsureSuccessStatusCode();

                JsonDocument resJSON = await JsonDocument.ParseAsync(res.Content.ReadAsStream());
                
                foreach (JsonElement project in resJSON.RootElement.GetProperty("projects").EnumerateArray())
                {
                    if (project.TryGetProperty("user_id", out var userIDElem))
                    {
                        Console.WriteLine($"Found username: u_{userIDElem.GetString()}");
                        return new MQTTCredentials()
                        {
                            Username = $"u_{userIDElem.GetString()}",
                            Token = token
                        };
                    }
                }
                throw new NotSupportedException("Could not obtain internal username (u_id) using projects API nor JWT.");
            }

            // Get the username from the JWT.
            string plainAccessTokenPayload = JWTSections.ElementAt(1);

            plainAccessTokenPayload += new string('=', (4 - (plainAccessTokenPayload.Length % 4)) % 4);

            plainAccessTokenPayload = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(plainAccessTokenPayload));

            var jsonAccessTokenPayload = JsonDocument.Parse(plainAccessTokenPayload);

            string username = jsonAccessTokenPayload.RootElement.GetProperty("username").GetString()
                ?? throw new NullReferenceException("accessToken does not contain a username! Has the schema changed?");

            return new MQTTCredentials()
            {
                Username = username,
                Token = token
            };
        }
    }
}
