using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SQLite
{
    public static class Database
    {
        public static void Initialize()
        {
            Directory.CreateDirectory(DbPaths.AppFolder);

            using var con = new SqliteConnection(GetConnectionString());
            con.Open();

            // Good defaults (WAL helps concurrency)
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
                                    PRAGMA journal_mode = WAL;
                                    PRAGMA synchronous = NORMAL;

                                    CREATE TABLE IF NOT EXISTS Rules (
                                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        RuleType TEXT NOT NULL, 
                                        Name TEXT NOT NULL,
                                        Json TEXT NOT NULL,
                                        CreatedAt TEXT NOT NULL
                                    );


                                    CREATE TABLE IF NOT EXISTS Settings (
                                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        ComPort TEXT NOT NULL, 
                                        MoxIP TEXT NOT NULL,
                                        ConfidenceLevel TEXT NOT NULL,
                                        DatabaseURL TEXT NOT NULL,
                                        BackOfficeURL TEXT NOT NULL
                                    );";
                cmd.ExecuteNonQuery();
            }
        }

        private static string GetConnectionString()
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = DbPaths.DbFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };
            return csb.ToString();
        }
    }

}

