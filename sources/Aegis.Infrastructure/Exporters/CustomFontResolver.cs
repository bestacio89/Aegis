using PdfSharp.Fonts;
using System;
using System.IO;

namespace Aegis.Infrastructure.Exporters
{
    public class CustomFontResolver : IFontResolver
    {
        public string DefaultFontName => "DejaVu Sans";

        public byte[] GetFont(string faceName)
        {
            return faceName switch
            {
                "DejaVu Sans#Regular" => File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf"),
                "DejaVu Sans#Bold" => File.ReadAllBytes(@"C:\Windows\Fonts\arialbd.ttf"),
                "DejaVu Sans#Italic" => File.ReadAllBytes(@"C:\Windows\Fonts\ariali.ttf"),
                "DejaVu Sans#BoldItalic" => File.ReadAllBytes(@"C:\Windows\Fonts\arialbi.ttf"),

                "Courier New#Regular" => File.ReadAllBytes(@"C:\Windows\Fonts\cour.ttf"),
                "Courier New#Bold" => File.ReadAllBytes(@"C:\Windows\Fonts\courbd.ttf"),
                "Courier New#Italic" => File.ReadAllBytes(@"C:\Windows\Fonts\couri.ttf"),
                "Courier New#BoldItalic" => File.ReadAllBytes(@"C:\Windows\Fonts\courbi.ttf"),

                _ => File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf"), // fallback
            };
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string suffix = (isBold, isItalic) switch
            {
                (true, true) => "BoldItalic",
                (true, false) => "Bold",
                (false, true) => "Italic",
                _ => "Regular",
            };

            // Route any requested family to the face we actually have bytes for
            string baseName = familyName switch
            {
                "Courier New" => "Courier New",
                _ => "DejaVu Sans", // covers "DejaVu Sans" and any unmapped family
            };

            return new FontResolverInfo($"{baseName}#{suffix}");
        }
    }
}