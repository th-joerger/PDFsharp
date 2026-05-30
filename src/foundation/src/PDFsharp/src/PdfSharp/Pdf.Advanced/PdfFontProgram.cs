// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Text;
using PdfSharp.Fonts;
using PdfSharp.Fonts.OpenType;
using PdfSharp.Pdf.Filters;

namespace PdfSharp.Pdf.Advanced
{
    /// <summary>
    /// Represents the base class of a PDF font.
    /// </summary>
    public sealed class PdfFontProgram : PdfDictionary
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PdfFontProgram"/> class.
        /// </summary>
        internal PdfFontProgram(PdfDocument document)
            : base(document)
        { }

        internal void CreateFontFileAndAddToDescriptor(PdfFontDescriptor pdfFontDescriptor, CMapInfo cmapInfo, bool cidFont)
        {
            OpenTypeFontFace subSet = pdfFontDescriptor.Descriptor.FontFace.CreateFontSubset(cmapInfo.GlyphIndices, cidFont);

            // CFF/OTF fonts have no loca table; TrueType fonts always have one.
            bool isCff = subSet.loca == null;

            byte[] fontData;
            if (isCff && subSet.TableDictionary.TryGetValue("CFF ", out var cffEntry))
            {
                // Extract just the CFF table bytes (more spec-compliant for FontFile3).
                fontData = new byte[cffEntry.Length];
                Array.Copy(subSet.FontSource.Bytes, cffEntry.Offset, fontData, 0, cffEntry.Length);
            }
            else
            {
                fontData = subSet.FontSource.Bytes;
            }

            Owner.Internals.AddObject(this);

            if (isCff)
            {
                pdfFontDescriptor.Elements[PdfFontDescriptor.Keys.FontFile3] = Reference;
                Elements["/Subtype"] = new PdfName(cidFont ? "/CIDFontType0C" : "/Type1C");
            }
            else
            {
                pdfFontDescriptor.Elements[PdfFontDescriptor.Keys.FontFile2] = Reference;
                Elements["/Length1"] = new PdfInteger(fontData.Length);
            }

            if (!Owner.Options.NoCompression)
            {
                fontData = Filtering.FlateDecode.Encode(fontData, _document.Options.FlateEncodeMode);
                Elements["/Filter"] = new PdfName("/FlateDecode");
            }
            Elements["/Length"] = new PdfInteger(fontData.Length);
            CreateStream(fontData);
        }
    }
}