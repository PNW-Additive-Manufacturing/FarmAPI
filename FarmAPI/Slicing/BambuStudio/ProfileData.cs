using System.Text.Json.Nodes;

namespace FarmAPI.Slicing.BambuStudio
{

    public class ProfileData
    {
        public JsonObject InnerOject { get; }

        public ProfileData(JsonObject jsonProfile, bool useDefaults = true)
        {
            this.InnerOject = jsonProfile;

            if (useDefaults)
            {
                this.SetDefaults();
            }
        }
        public ProfileData(Stream stream, bool useDefaults = true) : this(JsonNode.Parse(stream)!.AsObject(), useDefaults) { }

        public static ProfileData Load(string profilePath)
        {
            using var profileFile = File.OpenRead(profilePath);
            return new ProfileData(profileFile);
        }

        public void SetDefaults()
        {
            // Update default values to am-provided.

            if (this.Type == ProfileType.Filament)
            {
                this.Supports = true;
                this.SupportsOnBuildPlateOnly = true;
                this.SupportType = "tree(auto)";
            }

        }

        /// <summary>
        /// Bambu Studio JSON configuration files store booleans in a string containing "0" (false) or "1" (true) instead of using true and false.
        /// </summary>
        protected static bool ParseBooleanValue(string content)
        {
            if (string.Equals("0", content)) return false;
            else if (string.Equals("1", content)) return true;
            else throw new Exception($"Invalid boolean value supplied (Must be \"0\" or \"1\"): \"{content}\"");
        }


        private static readonly string[] IgnoredCombinedProperties = ["type", "inherits", "from"];
        internal static ProfileData Combine(ProfileData super, ProfileData sub)
        {
            var combinedProfile = super.InnerOject.DeepClone().AsObject();

            foreach (var property in sub.InnerOject)
            {
                if (IgnoredCombinedProperties.Contains(property.Key)) continue;

                combinedProfile[property.Key] = property.Value?.DeepClone();
            }
            return new ProfileData(combinedProfile, false);
        }

        public ProfileData Resolve(BambuStudio bambuStudio, BambuStudioManufacturerProfile manufacturerProfile)
        {
            if (this.Inherits != null)
            {
                string inheritedFilePath = Path.Join(manufacturerProfile.GetDirectory(bambuStudio), manufacturerProfile.GetRelativePath(this.Inherits, this.Type));

                using var inheritedFile = File.OpenRead(inheritedFilePath);
                var inheritedProfile = new ProfileData(inheritedFile, false);

                return Combine(inheritedProfile, this).Resolve(bambuStudio, manufacturerProfile);
            }
            else
            {
                this.SetDefaults();
                return this;
            }
        }

        public string Name => this.InnerOject["name"]!.GetValue<string>();

        public ProfileType Type => Enum.Parse<ProfileType>(this.InnerOject["type"]!.GetValue<string>(), true);

        public string? Inherits
        {
            get => this.InnerOject.TryGetPropertyValue("inherits", out var inheritsValue) ? inheritsValue!.GetValue<string>() : null;
        }

        public int Walls
        {
            get
            {
                if (this.InnerOject.TryGetPropertyValue("wall_loops", out var supportValue))
                {
                    return supportValue!.GetValue<int>();
                }
                return 0;
            }
            set
            {
                if (this.InnerOject.TryGetPropertyValue("wall_loops", out var supportValue))
                {
                    supportValue!.ReplaceWith(JsonValue.Create(value));
                }
                else this.InnerOject.Add("wall_loops", JsonValue.Create(value));
            }
        }

        public bool Supports
        {
            get
            {
                if (this.InnerOject.TryGetPropertyValue("enable_support", out var supportValue))
                {
                    return ParseBooleanValue(supportValue!.GetValue<string>());
                }
                return false;
            }
            set
            {
                if (this.InnerOject.TryGetPropertyValue("enable_support", out var supportValue))
                {
                    supportValue!.ReplaceWith(JsonValue.Create(value ? "1" : "0"));
                }
                else this.InnerOject.Add("enable_support", JsonValue.Create(value ? "1" : "0"));
            }
        }

        public bool SupportsOnBuildPlateOnly
        {
            get
            {
                if (this.InnerOject.TryGetPropertyValue("support_on_build_plate_only", out var supportValue))
                {
                    return ParseBooleanValue(supportValue!.GetValue<string>());
                }
                return false;
            }
            set
            {
                if (this.InnerOject.TryGetPropertyValue("support_on_build_plate_only", out var supportValue))
                {
                    supportValue!.ReplaceWith(JsonValue.Create(value ? "1" : "0"));
                }
                else this.InnerOject.Add("support_on_build_plate_only", JsonValue.Create(value ? "1" : "0"));
            }
        }

        /// <summary>
        /// Formattted as mode(auto) where mode is normal or tree. 
        /// </summary>
        public string SupportType
        {
            get
            {
                if (this.InnerOject.TryGetPropertyValue("support_type", out var supportValue))
                {
                    return supportValue!.GetValue<string>();
                }
                return "normal(auto)";
            }
            set
            {
                if (this.InnerOject.TryGetPropertyValue("support_type", out var supportValue))
                {
                    supportValue!.ReplaceWith(JsonValue.Create(value));
                }
                else this.InnerOject.Add("support_type", JsonValue.Create(value));
            }
        }
    }
}
