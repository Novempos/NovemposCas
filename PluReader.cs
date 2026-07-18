namespace CasScaleSender
{
    // Import formatindaki kolon adlarini tutar (ExcelReader/PluBuilder/XlsxWriter/
    // JsonPluIo ile uyumlu). Eskiden burada CasNetReader'dan once, teraziye
    // ReadPLU ile gelen V06 string'ini cozen bir Parse() metodu da vardi; okuma
    // artik CasNetReader'in kendi ParseFields'i ile yapiliyor (bkz. CasNetReader.cs),
    // bu yuzden Parse() (ve ona ozel Num/Txt yardimcilari) kaldirildi.
    public static class PluReader
    {
        // Import dosyasindaki kolon sirasi (ExcelReader/PluBuilder ile uyumlu).
        public static readonly string[] Columns =
        {
            "Department No", "PLU No", "PLU Type", "ItemCode", "Name", "Name2", "Name3",
            "Group No", "ExeBarcode", "Label No", "Aux Label No", "Origin No",
            "Unit Weight", "FixedWeight", "Prefix", "Pieces", "Qty Unit No", "Price",
            "Use Fixed Price Type"
        };
    }
}
