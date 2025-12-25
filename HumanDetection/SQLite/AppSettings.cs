using System;
using System.Collections.Generic;
using System.Text;

namespace SQLite
{
    public class AppSettings
    {
        public int Id { get; set; }
        public string ComPort { get; set; }
        public string MoxIP { get; set; }
        public string ConfidenceLevel { get; set; }
        public string DatabaseURL { get; set; }
        public string BackOfficeURL { get; set; }
        public int? RoutatorTimer { get; set; }
    }
}
