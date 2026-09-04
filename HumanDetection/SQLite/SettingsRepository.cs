using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;

namespace SQLite
{
   
        public static class SettingsRepository
        {
            private static SqliteConnection GetConnection()
                => new SqliteConnection(DatabaseConnection());

        private static string DatabaseConnection()
            => new SqliteConnectionStringBuilder
            {
                DataSource = DbPaths.DbFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

        // READ (Get first row – only one settings record)
        public static AppSettings GetSettings()
        {
            using var con = GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id, ComPort, MoxIP, RoutatorTimer, ConfidenceLevel, DatabaseURL, BackOfficeURL, AuthToken, ApiKey 
                        FROM Settings 
                        LIMIT 1";

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return null;

            return new AppSettings
            {
                Id = reader.GetInt32(0),
                ComPort = reader.GetString(1),
                MoxIP = reader.GetString(2),

                // ✅ Best handling
                RoutatorTimer = reader.IsDBNull(3)
                    ? 0
                    : Convert.ToInt32(reader.GetValue(3)),

                ConfidenceLevel = reader.IsDBNull(4)
                    ? "0"
                    : reader.GetInt32(4).ToString(),

                DatabaseURL = reader.IsDBNull(5)
                    ? string.Empty
                    : reader.GetString(5),

                BackOfficeURL = reader.IsDBNull(6)
                    ? string.Empty
                    : reader.GetString(6),

                AuthToken = reader.IsDBNull(7)
                    ? string.Empty
                    : reader.GetString(7),

                ApiKey = reader.IsDBNull(8)
                    ? string.Empty
                    : reader.GetString(8)
            };
        }

        // CREATE
        public static void InsertSettings(AppSettings s)
            {
                using var con = GetConnection();
                con.Open();

                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
                INSERT INTO Settings 
                (ComPort, MoxIP, ConfidenceLevel, DatabaseURL, BackOfficeURL,RoutatorTimer, AuthToken, ApiKey)
                VALUES (@ComPort,@MoxIP,@Confidence,@Db,@Back,@Timer,@AuthToken,@ApiKey)";

                cmd.Parameters.AddWithValue("@ComPort", s.ComPort);
                cmd.Parameters.AddWithValue("@MoxIP", s.MoxIP);
                cmd.Parameters.AddWithValue("@Confidence", s.ConfidenceLevel);
                cmd.Parameters.AddWithValue("@Db", s.DatabaseURL);
                cmd.Parameters.AddWithValue("@Back", s.BackOfficeURL);
                cmd.Parameters.AddWithValue("@Timer", s.RoutatorTimer);
                cmd.Parameters.AddWithValue("@AuthToken", s.AuthToken ?? string.Empty);
                cmd.Parameters.AddWithValue("@ApiKey", s.ApiKey ?? string.Empty);

            cmd.ExecuteNonQuery();
            }

            // UPDATE
            public static void UpdateSettings(AppSettings s)
            {
                using var con = GetConnection();
                con.Open();

                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
                UPDATE Settings SET
                    ComPort=@ComPort,
                    MoxIP=@MoxIP,
                    ConfidenceLevel=@Confidence,
                    DatabaseURL=@Db,
                    BackOfficeURL=@Back,
                    RoutatorTimer=@Time,
                    AuthToken=@AuthToken,
                    ApiKey=@ApiKey
                WHERE Id=@Id";

                cmd.Parameters.AddWithValue("@Id", s.Id);
                cmd.Parameters.AddWithValue("@ComPort", s.ComPort);
                cmd.Parameters.AddWithValue("@MoxIP", s.MoxIP);
                cmd.Parameters.AddWithValue("@Confidence", s.ConfidenceLevel);
                cmd.Parameters.AddWithValue("@Db", s.DatabaseURL);
                cmd.Parameters.AddWithValue("@Back", s.BackOfficeURL);
                 cmd.Parameters.AddWithValue("@Time", s.RoutatorTimer);
                cmd.Parameters.AddWithValue("@AuthToken", s.AuthToken ?? string.Empty);
                cmd.Parameters.AddWithValue("@ApiKey", s.ApiKey ?? string.Empty);


            cmd.ExecuteNonQuery();
            }
        public static void UpdateConfidenceThresHoldSettings(AppSettings s)
        {
            using var con = GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                UPDATE Settings SET                   
                    ConfidenceLevel=@Confidence              
                WHERE Id=@Id";

            cmd.Parameters.AddWithValue("@Id", s.Id);
            cmd.Parameters.AddWithValue("@Confidence", s.ConfidenceLevel);
 


            cmd.ExecuteNonQuery();
        }
        public static void UpdateRotatorSettings(AppSettings s)
        {
            using var con = GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
        UPDATE Settings SET                   
            RoutatorTimer=@Timer             
        WHERE Id=@Id";

            cmd.Parameters.AddWithValue("@Id", s.Id);
            cmd.Parameters.AddWithValue("@Timer", s.RoutatorTimer);

            cmd.ExecuteNonQuery();
        }

        public static List<RuleModel> GetAll()
        {
            var list = new List<RuleModel>();
           
            using var con = GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id, RuleName, Rule FROM Rules ORDER BY Id DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new RuleModel
                {
                    Id = reader.GetInt32(0),
                    RuleName = reader.GetString(1),
                    Rule = reader.GetString(2)
                });
            }

            return list;
        }

        // INSERT
        public static void Insert(string ruleName, string rule)
        {
            using var con = GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Rules (RuleName, Rule)
                VALUES (@name, @rule)";

            cmd.Parameters.AddWithValue("@name", ruleName);
            cmd.Parameters.AddWithValue("@rule", rule);

            cmd.ExecuteNonQuery();
        }

        // DELETE
        public static void Delete(int id)
        {
            using var con = GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Rules WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);

            cmd.ExecuteNonQuery();
        }
    }
    
}
