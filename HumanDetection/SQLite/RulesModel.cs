using System;
using System.Collections.Generic;
using System.Text;

namespace SQLite
{
    public class RuleModel
    {
        public int Id { get; set; }
        public string RuleName { get; set; }
        public string Rule { get; set; }   // Regex
    }
    public class OCRModel
    {
        public string SupplierName { get; set; }
        public string ExpiryDate { get; set; }
    }
}
