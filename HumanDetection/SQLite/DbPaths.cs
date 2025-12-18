using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SQLite
{
    public static class DbPaths
    {
        public static string AppFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AdPort",
                "AdPortDB");

        public static string DbFile =>
            Path.Combine(AppFolder, "app.db");
    }
}
