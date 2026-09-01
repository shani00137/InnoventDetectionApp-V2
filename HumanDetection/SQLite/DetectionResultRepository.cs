using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;

namespace SQLite
{
    public static class DetectionResultRepository
    {
        private static SqliteConnection GetConnection()
            => new SqliteConnection(DatabaseConnection());

        private static string DatabaseConnection()
            => new SqliteConnectionStringBuilder
            {
                DataSource = DbPaths.DbFile,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

        public static void Insert(DetectionResultModel r)
        {
            using var con = GetConnection();
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO DetectionResults
                (ScanDate, Status, Score, TotalBoxes, PalletHeight, Weight,
                 HumanDetected, BarcodeCount, BarcodeList, DateCount, DateList,
                 OCRResult, EntryTime, ExitTime, ImagesPath, AnnotatedPath, ResultFilePath, Attempts,
                 Task1StartTime, Task1EndTime, Task2StartTime, Task2EndTime)
                VALUES
                (@ScanDate, @Status, @Score, @TotalBoxes, @PalletHeight, @Weight,
                 @HumanDetected, @BarcodeCount, @BarcodeList, @DateCount, @DateList,
                 @OCRResult, @EntryTime, @ExitTime, @ImagesPath, @AnnotatedPath, @ResultFilePath, @Attempts,
                 @Task1StartTime, @Task1EndTime, @Task2StartTime, @Task2EndTime)";

            cmd.Parameters.AddWithValue("@ScanDate", r.ScanDate);
            cmd.Parameters.AddWithValue("@Status", r.Status);
            cmd.Parameters.AddWithValue("@Score", r.Score);
            cmd.Parameters.AddWithValue("@TotalBoxes", r.TotalBoxes);
            cmd.Parameters.AddWithValue("@PalletHeight", r.PalletHeight);
            cmd.Parameters.AddWithValue("@Weight", r.Weight ?? "");
            cmd.Parameters.AddWithValue("@HumanDetected", r.HumanDetected ?? "");
            cmd.Parameters.AddWithValue("@BarcodeCount", r.BarcodeCount);
            cmd.Parameters.AddWithValue("@BarcodeList", r.BarcodeList ?? "");
            cmd.Parameters.AddWithValue("@DateCount", r.DateCount);
            cmd.Parameters.AddWithValue("@DateList", r.DateList ?? "");
            cmd.Parameters.AddWithValue("@OCRResult", r.OCRResult ?? "");
            cmd.Parameters.AddWithValue("@EntryTime", r.EntryTime ?? "");
            cmd.Parameters.AddWithValue("@ExitTime", r.ExitTime ?? "");
            cmd.Parameters.AddWithValue("@ImagesPath", r.ImagesPath ?? "");
            cmd.Parameters.AddWithValue("@AnnotatedPath", r.AnnotatedPath ?? "");
            cmd.Parameters.AddWithValue("@ResultFilePath", r.ResultFilePath ?? "");
            cmd.Parameters.AddWithValue("@Attempts", r.Attempts);
            cmd.Parameters.AddWithValue("@Task1StartTime", r.Task1StartTime ?? "");
            cmd.Parameters.AddWithValue("@Task1EndTime", r.Task1EndTime ?? "");
            cmd.Parameters.AddWithValue("@Task2StartTime", r.Task2StartTime ?? "");
            cmd.Parameters.AddWithValue("@Task2EndTime", r.Task2EndTime ?? "");

            cmd.ExecuteNonQuery();
        }

        public static List<DetectionResultModel> GetAll()
        {
            var list = new List<DetectionResultModel>();

            using var con = GetConnection();
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT * FROM DetectionResults ORDER BY Id DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapReader(reader));
            }

            return list;
        }

        public static List<DetectionResultModel> GetByStatus(string status)
        {
            var list = new List<DetectionResultModel>();

            using var con = GetConnection();
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT * FROM DetectionResults WHERE Status=@status ORDER BY Id DESC";
            cmd.Parameters.AddWithValue("@status", status);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapReader(reader));
            }

            return list;
        }

        public static List<DetectionResultModel> GetByDateRange(DateTime from, DateTime to)
        {
            var list = new List<DetectionResultModel>();

            using var con = GetConnection();
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT * FROM DetectionResults WHERE ScanDate >= @from AND ScanDate <= @to ORDER BY Id DESC";
            cmd.Parameters.AddWithValue("@from", from);
            cmd.Parameters.AddWithValue("@to", to);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapReader(reader));
            }

            return list;
        }

        public static DetectionResultModel GetById(int id)
        {
            using var con = GetConnection();
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT * FROM DetectionResults WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapReader(reader);

            return null;
        }

        public static void Delete(int id)
        {
            using var con = GetConnection();
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM DetectionResults WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);

            cmd.ExecuteNonQuery();
        }

        private static DetectionResultModel MapReader(SqliteDataReader reader)
        {
            return new DetectionResultModel
            {
                Id = reader.GetInt32(0),
                ScanDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Score = reader.GetDouble(3),
                TotalBoxes = reader.GetInt32(4),
                PalletHeight = reader.GetDouble(5),
                Weight = reader.IsDBNull(6) ? "" : reader.GetString(6),
                HumanDetected = reader.IsDBNull(7) ? "" : reader.GetString(7),
                BarcodeCount = reader.GetInt32(8),
                BarcodeList = reader.IsDBNull(9) ? "" : reader.GetString(9),
                DateCount = reader.GetInt32(10),
                DateList = reader.IsDBNull(11) ? "" : reader.GetString(11),
                OCRResult = reader.IsDBNull(12) ? "" : reader.GetString(12),
                EntryTime = reader.IsDBNull(13) ? "" : reader.GetString(13),
                ExitTime = reader.IsDBNull(14) ? "" : reader.GetString(14),
                ImagesPath = reader.IsDBNull(15) ? "" : reader.GetString(15),
                AnnotatedPath = reader.IsDBNull(16) ? "" : reader.GetString(16),
                ResultFilePath = reader.IsDBNull(17) ? "" : reader.GetString(17),
                Attempts = reader.GetInt32(18),
                Task1StartTime = reader.IsDBNull(19) ? "" : reader.GetString(19),
                Task1EndTime = reader.IsDBNull(20) ? "" : reader.GetString(20),
                Task2StartTime = reader.IsDBNull(21) ? "" : reader.GetString(21),
                Task2EndTime = reader.IsDBNull(22) ? "" : reader.GetString(22)
            };
        }
    }
}
