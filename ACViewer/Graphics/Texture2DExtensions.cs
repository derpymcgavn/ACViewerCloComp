using System;
using Microsoft.Xna.Framework.Graphics;

namespace ACViewer.Graphics
{
    /// <summary>
    /// Extension helpers for Texture2D to make data upload more forgiving.
    /// </summary>
    public static class Texture2DExtensions
    {
        /// <summary>
        /// Automatically uploads raw pixel data to a Texture2D, expanding 24-bit RGB data
        /// to 32-bit RGBA (adding opaque alpha) when the texture expects SurfaceFormat.Color (RGBA32).
        /// Falls back to the normal SetData call when sizes already match.
        /// </summary>
        /// <param name="texture">Destination texture (must not be null).</param>
        /// <param name="pixelData">Pixel byte data (RGB or RGBA).</param>
        /// <param name="assumeOpaqueAlpha">Alpha value to use when expanding RGB to RGBA (default 255).</param>
        public static void SetDataAutoExpand(this Texture2D texture, byte[] pixelData, byte assumeOpaqueAlpha = 255)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (pixelData == null) throw new ArgumentNullException(nameof(pixelData));

            // Only handle the common case that triggered the original exception: uploading 24-bit data to a 32-bit texture.
            // Width * Height
            int pixelCount = texture.Width * texture.Height;
            int expectedRgbaBytes = pixelCount * 4;

            // If already correct size just pass through.
            if (pixelData.Length == expectedRgbaBytes)
            {
                texture.SetData(pixelData); // Let existing validation run.
                return;
            }

            int expectedRgbBytes = pixelCount * 3;
            if (pixelData.Length == expectedRgbBytes)
            {
                // Expand RGB -> RGBA
                var expanded = new byte[expectedRgbaBytes];
                int srcIndex = 0;
                int dstIndex = 0;
                for (int i = 0; i < pixelCount; i++)
                {
                    expanded[dstIndex++] = pixelData[srcIndex++]; // R
                    expanded[dstIndex++] = pixelData[srcIndex++]; // G
                    expanded[dstIndex++] = pixelData[srcIndex++]; // B
                    expanded[dstIndex++] = assumeOpaqueAlpha;     // A
                }
                texture.SetData(expanded);
                return;
            }

            // If neither matches, throw a clearer diagnostic before the internal ValidateParams does.
            throw new ArgumentException($"Pixel data length ({pixelData.Length}) does not match expected sizes for texture {texture.Width}x{texture.Height}. Expected {expectedRgbBytes} (RGB24) or {expectedRgbaBytes} (RGBA32).", nameof(pixelData));
        }
    }
}
