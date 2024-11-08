namespace FarmAPI
{
    public readonly struct Filament : IEquatable<Filament>
    {
        public string Color { get; }
        public string Material { get; }

        public Filament(string material, string hexColor)
        {
            if (string.IsNullOrWhiteSpace(material)) throw new ArgumentException($"Filament Material cannot be null or empty!");
            if (string.IsNullOrWhiteSpace(hexColor)) throw new ArgumentException($"Filament Color cannot be null or empty!");

            // Bambu Lab appends FF to the end of the hex color code on their machines - we will use the same.
            if (hexColor.Length == 6) hexColor = $"{hexColor}FF";

            // Loosly validate the hex color code.
            if (hexColor.Length != 8) throw new ArgumentException($"Filament Color ({hexColor}) must be a hex color code!");

            if (!hexColor.Substring(hexColor.Length - 2).Equals("FF"))
            {
                // Ending is not FF? Weird.
                hexColor = $"{hexColor[..6]}FF";
            }

            Color = hexColor;
            Material = material;
        }

        public override string ToString()
        {
            return $"{Material} {Color}";
        }

        public bool Equals(Filament other)
        {
            return Color.Equals(other.Color, StringComparison.OrdinalIgnoreCase) && Material.Equals(other.Material, StringComparison.OrdinalIgnoreCase);
        }
    }
}
