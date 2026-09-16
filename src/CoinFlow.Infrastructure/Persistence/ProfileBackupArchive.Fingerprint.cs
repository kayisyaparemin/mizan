using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

public sealed partial class ProfileBackupArchive
{
    public async Task<string> ComputeFingerprintAsync(
        CancellationToken cancellationToken = default)
    {
        // Dosyanın değişiklik zamanına değil içeriğine bakılır: store her
        // açılışta ayar satırını aynı değerlerle yeniden yazıyor, zaman
        // damgası "değişti" derdi. Son açılış tarihi de bilinçli olarak
        // dışarıda — yalnız profili açmak yeni bir yedek gerektirmez.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var profile in (await repository.GetProfilesAsync(cancellationToken))
                     .OrderBy(profile => profile.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, $"profile|{profile.Id:N}|{profile.Name}");
            var databasePath = repository.GetDatabasePath(profile.Id);
            if (File.Exists(databasePath))
            {
                AppendDatabaseContent(hash, databasePath);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendDatabaseContent(IncrementalHash hash, string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SQLiteConnection(
            new SQLiteConnectionString(databasePath, SQLiteOpenFlags.ReadOnly, true));
        connection.BusyTimeout = TimeSpan.FromSeconds(10);
        connection.RunInTransaction(() =>
        {
            var tables = connection.QueryScalars<string>(
                "SELECT name FROM sqlite_master " +
                "WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
            foreach (var table in tables)
            {
                Append(hash, $"table|{table}");
                var statement = SQLite3.Prepare2(
                    connection.Handle,
                    $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\" ORDER BY rowid");
                try
                {
                    while (SQLite3.Step(statement) == SQLite3.Result.Row)
                    {
                        var columns = SQLite3.ColumnCount(statement);
                        for (var column = 0; column < columns; column++)
                        {
                            Append(hash, SQLite3.ColumnType(statement, column) switch
                            {
                                SQLite3.ColType.Integer => "i" + SQLite3.ColumnInt64(statement, column)
                                    .ToString(CultureInfo.InvariantCulture),
                                SQLite3.ColType.Float => "f" + SQLite3.ColumnDouble(statement, column)
                                    .ToString("R", CultureInfo.InvariantCulture),
                                SQLite3.ColType.Text => "t" + SQLite3.ColumnString(statement, column),
                                SQLite3.ColType.Blob => "b" + Convert.ToBase64String(
                                    SQLite3.ColumnByteArray(statement, column)),
                                _ => "n"
                            });
                        }
                    }
                }
                finally
                {
                    SQLite3.Finalize(statement);
                }
            }
        });
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
