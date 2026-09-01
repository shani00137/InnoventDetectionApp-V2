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

            if (File.Exists(DbPaths.DbFile))
            {
                try
                {
                    var attrs = File.GetAttributes(DbPaths.DbFile);
                    if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(DbPaths.DbFile, attrs & ~FileAttributes.ReadOnly);
                    }
                }
                catch
                {
                    TryDelete(DbPaths.DbFile);
                    TryDelete(DbPaths.DbFile + "-wal");
                    TryDelete(DbPaths.DbFile + "-shm");
                }
            }

            if (File.Exists(DbPaths.DbFile))
            {
                try
                {
                    using var probe = new SqliteConnection(GetConnectionString());
                    probe.Open();
                }
                catch
                {
                    TryDelete(DbPaths.DbFile);
                    TryDelete(DbPaths.DbFile + "-wal");
                    TryDelete(DbPaths.DbFile + "-shm");
                }
            }

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
            Rule TEXT NOT NULL, 
            RuleName TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Settings (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ComPort TEXT NOT NULL, 
            MoxIP TEXT NOT NULL,
            RoutatorTimer INTEGER NOT NULL,
            ConfidenceLevel TEXT NOT NULL,
            DatabaseURL TEXT NOT NULL,
            BackOfficeURL TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS DetectionResults (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ScanDate TEXT NOT NULL,
            Status TEXT NOT NULL,
            Score REAL NOT NULL,
            TotalBoxes INTEGER NOT NULL,
            PalletHeight REAL NOT NULL,
            Weight TEXT,
            HumanDetected TEXT,
            BarcodeCount INTEGER NOT NULL,
            BarcodeList TEXT,
            DateCount INTEGER NOT NULL,
            DateList TEXT,
            OCRResult TEXT,
            EntryTime TEXT,
            ExitTime TEXT,
            ImagesPath TEXT,
            AnnotatedPath TEXT,
            ResultFilePath TEXT,
            Attempts INTEGER NOT NULL,
            Task1StartTime TEXT,
            Task1EndTime TEXT,
            Task2StartTime TEXT,
            Task2EndTime TEXT
        );
    ";

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

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }

}

