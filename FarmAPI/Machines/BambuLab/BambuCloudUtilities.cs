using Microsoft.AspNetCore.Authentication;
using System.Text;
using System.Text.Json;

namespace FarmAPI.Machines.BambuLab
{
    public class BambuCloudCredentials(string email, string password)
    {
        public string Email { get; set; } = email;
        public string Password { get; set; } = password;
    }

    public static class BambuCloudUtilities
    {
        private static readonly HttpClient bambuCloutHttpClient = new()
        {
            BaseAddress = new Uri("https://api.bambulab.com")
        };

        public static async Task<MQTTCredentials> FetchMQTTCredentials(BambuCloudCredentials credentials)
        {
            var response = await bambuCloutHttpClient.PostAsJsonAsync("/v1/user-service/user/login", new
            {
                account = credentials.Email,
                credentials.Password
            });
            response.EnsureSuccessStatusCode();

            var responseJson = await JsonDocument.ParseAsync(response.Content.ReadAsStream())
                ?? throw new NullReferenceException("Response body could not be parsed!");

            var accessToken = responseJson!.RootElement.GetProperty("accessToken").GetString()
                ?? throw new NullReferenceException("Response body does not contain an accessToken!");

            return FetchMQTTCredentials(accessToken);
        }

        public static MQTTCredentials FetchMQTTCredentials(string JWT)
        {
            // Read the account ID from the accessToken payload.
            string plainAccessTokenPayload = JWT.Split('.').ElementAt(1);

            plainAccessTokenPayload += new string('=', (4 - (plainAccessTokenPayload.Length % 4)) % 4);

            plainAccessTokenPayload = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(plainAccessTokenPayload));

            var jsonAccessTokenPayload = JsonDocument.Parse(plainAccessTokenPayload);

            string username = jsonAccessTokenPayload.RootElement.GetProperty("username").GetString()
                ?? throw new NullReferenceException("accessToken doess not contain a username! Has the schema changed?");

            return new MQTTCredentials()
            {
                Username = username,
                Token = JWT
            };
        }
    }
}
