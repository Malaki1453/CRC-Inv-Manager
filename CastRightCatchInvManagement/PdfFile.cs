namespace CastRightCatchInvManagement
{
    /// <summary>Wraps a drawn page into a one-page PDF and stores it.</summary>
    internal static class PdfFile
    {
        public static string Save(string kind, string key, string title, PdfDraw page)
        {
            string fileName = Sanitize(title + ".pdf");
            return DataFiles.SaveStoredPdf(kind, (key ?? "").Trim(), fileName, page.ToPdf());
        }

        public static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return name;
        }
    }
}
