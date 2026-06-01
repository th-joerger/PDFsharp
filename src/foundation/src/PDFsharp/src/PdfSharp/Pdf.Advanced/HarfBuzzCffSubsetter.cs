// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Runtime.InteropServices;

namespace PdfSharp.Pdf.Advanced
{
    /// <summary>
    /// Subsets CFF/OTF fonts using HarfBuzz hb-subset (libHarfBuzzSharp.dll).
    /// Falls back gracefully (returns null) if the native library is not available.
    /// </summary>
    static class HarfBuzzCffSubsetter
    {
        const string Lib = "libHarfBuzzSharp";

        // HB_MEMORY_MODE_READONLY = 0: HarfBuzz reads the data but does not take ownership.
        [DllImport(Lib)] static extern IntPtr hb_blob_create(IntPtr data, uint length, int mode, IntPtr userData, IntPtr destroy);
        [DllImport(Lib)] static extern IntPtr hb_face_create(IntPtr blob, uint index);
        [DllImport(Lib)] static extern IntPtr hb_subset_input_create_or_fail();
        [DllImport(Lib)] static extern IntPtr hb_subset_input_glyph_set(IntPtr input);
        [DllImport(Lib)] static extern void hb_set_add(IntPtr set, uint codepoint);
        // HB_SUBSET_FLAGS_RETAIN_GIDS = 2: preserve original glyph IDs so PDF content stream references remain valid.
        [DllImport(Lib)] static extern void hb_subset_input_set_flags(IntPtr input, uint flags);
        [DllImport(Lib)] static extern IntPtr hb_subset_or_fail(IntPtr face, IntPtr input);
        [DllImport(Lib)] static extern IntPtr hb_face_reference_blob(IntPtr face);
        [DllImport(Lib)] static extern IntPtr hb_blob_get_data(IntPtr blob, out uint length);
        [DllImport(Lib)] static extern void hb_blob_destroy(IntPtr blob);
        [DllImport(Lib)] static extern void hb_face_destroy(IntPtr face);
        [DllImport(Lib)] static extern void hb_subset_input_destroy(IntPtr input);

        /// <summary>
        /// Subsets a CFF OTF font to only the specified glyph IDs.
        /// Returns the subsetted CFF table bytes, or null if subsetting is unavailable or fails
        /// (caller should then fall back to embedding the full CFF table).
        /// </summary>
        internal static byte[]? TrySubsetCff(byte[] otfBytes, IEnumerable<ushort> glyphIds)
        {
            var pinnedData = GCHandle.Alloc(otfBytes, GCHandleType.Pinned);
            var blobPtr = IntPtr.Zero;
            var facePtr = IntPtr.Zero;
            var inputPtr = IntPtr.Zero;
            var subsetFacePtr = IntPtr.Zero;
            var subsetBlobPtr = IntPtr.Zero;

            try
            {
                blobPtr = hb_blob_create(pinnedData.AddrOfPinnedObject(), (uint)otfBytes.Length, 0, IntPtr.Zero, IntPtr.Zero);
                if (blobPtr == IntPtr.Zero) return null;

                facePtr = hb_face_create(blobPtr, 0);
                if (facePtr == IntPtr.Zero) return null;

                inputPtr = hb_subset_input_create_or_fail();
                if (inputPtr == IntPtr.Zero) return null;

                // RETAIN_GIDS: PDFsharp encodes original glyph IDs in the content stream;
                // without this flag HarfBuzz remaps IDs sequentially and all references break.
                const uint HB_SUBSET_FLAGS_RETAIN_GIDS = 2u;
                hb_subset_input_set_flags(inputPtr, HB_SUBSET_FLAGS_RETAIN_GIDS);

                var glyphSet = hb_subset_input_glyph_set(inputPtr);
                hb_set_add(glyphSet, 0); // always include .notdef (glyph 0)
                foreach (var glyph in glyphIds)
                    hb_set_add(glyphSet, glyph);

                subsetFacePtr = hb_subset_or_fail(facePtr, inputPtr);
                if (subsetFacePtr == IntPtr.Zero) return null;

                subsetBlobPtr = hb_face_reference_blob(subsetFacePtr);
                if (subsetBlobPtr == IntPtr.Zero) return null;

                var dataPtr = hb_blob_get_data(subsetBlobPtr, out var length);
                if (dataPtr == IntPtr.Zero || length == 0) return null;

                var subsetOtfBytes = new byte[length];
                Marshal.Copy(dataPtr, subsetOtfBytes, 0, (int)length);

                // Extract just the CFF table from the subsetted OTF for /FontFile3 /Type1C embedding.
                return ExtractCffTable(subsetOtfBytes);
            }
            catch
            {
                // DLL not found, subsetting failed, or any other error → caller falls back to full font.
                return null;
            }
            finally
            {
                if (subsetBlobPtr != IntPtr.Zero) hb_blob_destroy(subsetBlobPtr);
                if (subsetFacePtr != IntPtr.Zero) hb_face_destroy(subsetFacePtr);
                if (inputPtr != IntPtr.Zero) hb_subset_input_destroy(inputPtr);
                if (facePtr != IntPtr.Zero) hb_face_destroy(facePtr);
                if (blobPtr != IntPtr.Zero) hb_blob_destroy(blobPtr);
                pinnedData.Free();
            }
        }

        /// <summary>
        /// Parses the OTF table directory to locate and extract the 'CFF ' table bytes.
        /// Returns null if the table is not found or the data is invalid.
        /// </summary>
        static byte[]? ExtractCffTable(byte[] otfBytes)
        {
            if (otfBytes.Length < 12) return null;
            int numTables = (otfBytes[4] << 8) | otfBytes[5];
            for (int i = 0; i < numTables; i++)
            {
                int r = 12 + i * 16;
                if (r + 16 > otfBytes.Length) break;
                // Tag = 'CFF ' (0x43 0x46 0x46 0x20)
                if (otfBytes[r] == 'C' && otfBytes[r + 1] == 'F' && otfBytes[r + 2] == 'F' && otfBytes[r + 3] == ' ')
                {
                    int offset = (otfBytes[r + 8] << 24) | (otfBytes[r + 9] << 16) | (otfBytes[r + 10] << 8) | otfBytes[r + 11];
                    int length = (otfBytes[r + 12] << 24) | (otfBytes[r + 13] << 16) | (otfBytes[r + 14] << 8) | otfBytes[r + 15];
                    if (offset + length <= otfBytes.Length)
                    {
                        var cff = new byte[length];
                        Array.Copy(otfBytes, offset, cff, 0, length);
                        return cff;
                    }
                }
            }
            return null;
        }
    }
}
