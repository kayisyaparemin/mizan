using System.IO.Compression;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Infrastructure.Persistence;
using SQLite;

namespace CoinFlow.Tests;

/// <summary>
/// Yedeğin sözü: uygulama kaldırılıp kurulduğunda yedekten dönen her profil
/// bıraktığı gibidir; bozuk ya da yabancı bir dosya hiçbir iz bırakmaz.
/// </summary>
public sealed class BackupTests
{
    private static readonly DateOnly Today = new(2026, 9, 14);

    [Fact]
    public async Task Backup_RestoresEveryProfileAsItWas_EvenWhileOneIsOpen()
    {
        await WithRoots(async (source, target) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            var ayse = await app.Profiles.CreateAsync("Ayşe");
            var mehmet = await app.Profiles.CreateAsync("Mehmet");
            var bos = await app.Profiles.CreateAsync("Hiç Açılmadı");
            var service = TestFactory.Service(app.Store, Today);
            await app.Profiles.OpenAsync(mehmet.Id);
            await app.Profiles.OpenAsync(ayse.Id);
            await service.LoadCanonicalDevelopmentDataAsync();
            var plan = await service.GetFinancialPlanAsync();

            // Ayşe'nin profili açıkken yedek alınır: kullanıcı gece
            // uygulamayı açık bırakmış olabilir.
            using var backup = new MemoryStream();
            var written = await app.Archive.WriteAsync(backup);
            Assert.Equal(["Ayşe", "Mehmet", "Hiç Açılmadı"], written.ProfileNames);

            await using var reinstalled = Installation.Create(target, clock);
            backup.Position = 0;
            var restored = await reinstalled.Backup.RestoreAsync(backup);

            Assert.Equal(written.ProfileNames, restored.ProfileNames);
            var profiles = await reinstalled.Profiles.GetProfilesAsync();
            Assert.Equal(
                [ayse.Id, mehmet.Id, bos.Id],
                profiles.Select(profile => profile.Id));
            Assert.False(File.Exists(reinstalled.Repository.GetDatabasePath(bos.Id)));

            var restoredService = TestFactory.Service(reinstalled.Store, Today);
            await reinstalled.Profiles.OpenAsync(ayse.Id);
            Assert.False(await restoredService.IsOnboardingRequiredAsync());
            var restoredPlan = await restoredService.GetFinancialPlanAsync();
            Assert.Equal(plan.Salaries.Count, restoredPlan.Salaries.Count);
            Assert.Equal(plan.Loans.Count, restoredPlan.Loans.Count);
            Assert.Equal(plan.CreditCards.Count, restoredPlan.CreditCards.Count);
            Assert.Equal(
                plan.Settings.MonthlyLivingBudget,
                restoredPlan.Settings.MonthlyLivingBudget);

            await reinstalled.Profiles.OpenAsync(mehmet.Id);
            Assert.True(await restoredService.IsOnboardingRequiredAsync());

            await reinstalled.Profiles.OpenAsync(bos.Id);
            Assert.True(await restoredService.IsOnboardingRequiredAsync());
        });
    }

    [Fact]
    public async Task Restore_IsRefused_WhenTheInstallationAlreadyHasAProfile()
    {
        await WithRoots(async (source, target) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            await app.Profiles.CreateAsync("Ayşe");
            using var backup = new MemoryStream();
            await app.Archive.WriteAsync(backup);

            await using var other = Installation.Create(target, clock);
            var existing = await other.Profiles.CreateAsync("Mehmet");
            backup.Position = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => other.Backup.RestoreAsync(backup));

            Assert.Equal(existing.Id, Assert.Single(await other.Profiles.GetProfilesAsync()).Id);
        });
    }

    [Fact]
    public async Task Restore_RejectsForeignAndDamagedFiles_AndLeavesNothingBehind()
    {
        await WithRoots(async (source, target) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            var ayse = await app.Profiles.CreateAsync("Ayşe");
            await app.Profiles.OpenAsync(ayse.Id);
            await app.Profiles.CloseAsync();
            using var valid = new MemoryStream();
            await app.Archive.WriteAsync(valid);

            var notAZip = new MemoryStream([1, 2, 3, 4, 5]);
            var zipWithoutManifest = Zip(archive =>
                archive.CreateEntry("fotograf.jpg"));
            var damagedDatabase = Rewrite(valid, (archive, entryName) =>
            {
                if (entryName.EndsWith(".db3", StringComparison.Ordinal))
                {
                    using var stream = archive.CreateEntry(entryName).Open();
                    stream.Write(new byte[8192]);
                    return true;
                }

                return false;
            });

            await using var reinstalled = Installation.Create(target, clock);
            foreach (var file in new[] { notAZip, zipWithoutManifest, damagedDatabase })
            {
                file.Position = 0;
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => reinstalled.Backup.RestoreAsync(file));

                Assert.Empty(await reinstalled.Profiles.GetProfilesAsync());
                Assert.Empty(Directory.GetDirectories(target, ".restore-*"));
            }
        });
    }

    /// <summary>
    /// "Eski hâline bakmak" senaryosu: yedek alındıktan sonra profil
    /// değişti. Yedekten eklemek mevcut profili değiştirmez; yedekteki hâli
    /// ayrı bir profil olarak gelir.
    /// </summary>
    [Fact]
    public async Task AddFromBackup_WhenTheProfileExists_AddsACopy_AndLeavesTheCurrentOneAlone()
    {
        await WithRoots(async (source, _) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            var ayse = await app.Profiles.CreateAsync("Ayşe");
            await app.Profiles.CreateAsync("Mehmet");
            await app.Profiles.OpenAsync(ayse.Id);
            var service = TestFactory.Service(app.Store, Today);
            await service.LoadCanonicalDevelopmentDataAsync();
            using var backup = new MemoryStream();
            var written = await app.Archive.WriteAsync(backup);

            var settings = await app.Store.GetSettingsAsync();
            await app.Store.SaveSettingsAsync(settings with { MonthlyLivingBudget = 99_999m });
            await app.Profiles.CloseAsync();

            var result = await app.Backup.AddFromBackupAsync(backup, [ayse.Id]);

            var copy = Assert.Single(result.Added);
            Assert.True(copy.IsCopy);
            Assert.NotEqual(ayse.Id, copy.Profile.Id);
            Assert.Equal(BackupService.CopyName("Ayşe", written.CreatedAt), copy.Profile.Name);
            Assert.EndsWith("yedeği)", copy.Profile.Name);
            Assert.Equal(3, (await app.Profiles.GetProfilesAsync()).Count);

            await app.Profiles.OpenAsync(ayse.Id);
            Assert.Equal(99_999m, (await app.Store.GetSettingsAsync()).MonthlyLivingBudget);
            await app.Profiles.OpenAsync(copy.Profile.Id);
            Assert.Equal(30_000m, (await app.Store.GetSettingsAsync()).MonthlyLivingBudget);
        });
    }

    /// <summary>
    /// Yeniden kurulumda yanlışlıkla profil açılmış: yedekteki profiller
    /// kendi kimlikleriyle gelir; aynı adı taşıyan başka bir profil varsa ad
    /// numaralanır.
    /// </summary>
    [Fact]
    public async Task AddFromBackup_OnAnotherInstallation_KeepsIdentities_AndNumbersClashingNames()
    {
        await WithRoots(async (source, target) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            var ayse = await app.Profiles.CreateAsync("Ayşe");
            var mehmet = await app.Profiles.CreateAsync("Mehmet");
            await app.Profiles.OpenAsync(mehmet.Id);
            await app.Profiles.CloseAsync();
            using var backup = new MemoryStream();
            await app.Archive.WriteAsync(backup);

            await using var reinstalled = Installation.Create(target, clock);
            var accidental = await reinstalled.Profiles.CreateAsync("ayşe");

            var result = await reinstalled.Backup.AddFromBackupAsync(backup, profileIds: null);

            Assert.All(result.Added, added => Assert.False(added.IsCopy));
            Assert.Equal(
                [(ayse.Id, "Ayşe 2"), (mehmet.Id, "Mehmet")],
                result.Added.Select(added => (added.Profile.Id, added.Profile.Name)));
            var profiles = await reinstalled.Profiles.GetProfilesAsync();
            Assert.Equal(3, profiles.Count);
            Assert.Contains(profiles, profile => profile.Id == accidental.Id && profile.Name == "ayşe");
            await reinstalled.Profiles.OpenAsync(mehmet.Id);
        });
    }

    [Fact]
    public async Task AddFromBackup_RejectsUnknownSelections_AndDamagedData_WithoutAddingAnything()
    {
        await WithRoots(async (source, _) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            var ayse = await app.Profiles.CreateAsync("Ayşe");
            await app.Profiles.OpenAsync(ayse.Id);
            await app.Profiles.CloseAsync();
            using var valid = new MemoryStream();
            await app.Archive.WriteAsync(valid);
            var damaged = Rewrite(valid, (archive, entryName) =>
            {
                if (entryName.EndsWith(".db3", StringComparison.Ordinal))
                {
                    using var stream = archive.CreateEntry(entryName).Open();
                    stream.Write(new byte[8192]);
                    return true;
                }

                return false;
            });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => app.Backup.AddFromBackupAsync(valid, [Guid.NewGuid()]));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => app.Backup.AddFromBackupAsync(damaged, profileIds: null));

            Assert.Single(await app.Profiles.GetProfilesAsync());
            Assert.Empty(Directory.GetDirectories(source, ".restore-*"));
        });
    }

    [Fact]
    public void CopyName_FitsTheNameLimit()
    {
        var at = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("Ayşe (14 Eylül yedeği)", BackupService.CopyName("Ayşe", at));
        var longName = BackupService.CopyName("Ailenin ortak bütçe profili", at);
        Assert.True(longName.Length <= UserProfile.MaxNameLength, longName);
        Assert.EndsWith("… (14 Eylül yedeği)", longName);
    }

    [Fact]
    public async Task Restore_RejectsABackupFromANewerSchema()
    {
        await WithRoots(async (source, target) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            var ayse = await app.Profiles.CreateAsync("Ayşe");
            await app.Profiles.OpenAsync(ayse.Id);
            await app.Profiles.CloseAsync();
            using (var connection = new SQLiteConnection(app.Repository.GetDatabasePath(ayse.Id)))
            {
                connection.Execute(
                    "UPDATE settings SET SchemaVersion = ?",
                    SqliteCoinFlowStore.CurrentSchemaVersion + 1);
            }

            using var backup = new MemoryStream();
            await app.Archive.WriteAsync(backup);

            await using var reinstalled = Installation.Create(target, clock);
            backup.Position = 0;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => reinstalled.Backup.RestoreAsync(backup));
            Assert.Contains("daha yeni", error.Message);
            Assert.Empty(await reinstalled.Profiles.GetProfilesAsync());
        });
    }

    [Fact]
    public async Task NightlyBackup_WritesOnlyWhenSomethingChanged_OneFilePerDay()
    {
        await WithRoots(async (source, _) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);

            Assert.Equal(
                BackupOutcome.NothingToBackUp,
                (await app.Backup.BackUpIfChangedAsync()).Outcome);

            var ayse = await app.Profiles.CreateAsync("Ayşe");
            await app.Profiles.OpenAsync(ayse.Id);
            var first = await app.Backup.BackUpIfChangedAsync();
            Assert.Equal(BackupOutcome.Created, first.Outcome);
            Assert.Equal("Mizan-yedek-2026-09-14.zip", first.State!.FileName);

            clock.Today = Today.AddDays(1);
            Assert.Equal(
                BackupOutcome.Unchanged,
                (await app.Backup.BackUpIfChangedAsync()).Outcome);

            // Yalnız profili yeniden açmak yedek gerektirmez.
            await app.Profiles.OpenAsync(ayse.Id);
            Assert.Equal(
                BackupOutcome.Unchanged,
                (await app.Backup.BackUpIfChangedAsync()).Outcome);

            var settings = await app.Store.GetSettingsAsync();
            await app.Store.SaveSettingsAsync(settings with { MonthlyLivingBudget = 12_345m });
            Assert.Equal(
                BackupOutcome.Created,
                (await app.Backup.BackUpIfChangedAsync()).Outcome);

            // Aynı gün "Şimdi Yedekle" o günün dosyasının üzerine yazar.
            Assert.Equal(BackupOutcome.Created, (await app.Backup.BackUpNowAsync()).Outcome);
            Assert.Equal(
                ["Mizan-yedek-2026-09-14.zip", "Mizan-yedek-2026-09-15.zip"],
                app.BackupFiles());

            // Yazılan dosya geri yüklenebilir bir yedek.
            using var latest = File.OpenRead(Path.Combine(app.BackupFolder, "Mizan-yedek-2026-09-15.zip"));
            using var zip = new ZipArchive(latest);
            Assert.NotNull(zip.GetEntry(ProfileBackupArchive.ManifestEntryName));
        });
    }

    [Fact]
    public async Task Backups_KeepTheNewestSeven()
    {
        await WithRoots(async (source, _) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            var ayse = await app.Profiles.CreateAsync("Ayşe");
            await app.Profiles.OpenAsync(ayse.Id);

            for (var day = 0; day < 9; day++)
            {
                clock.Today = Today.AddDays(day);
                await app.Backup.BackUpNowAsync();
            }

            Assert.Equal(
                Enumerable.Range(2, 7).Select(day => BackupService.FileNameFor(Today.AddDays(day))),
                app.BackupFiles());
        });
    }

    [Fact]
    public void Retention_NeverTouchesFilesMizanDidNotName()
    {
        var at = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var files = Enumerable.Range(0, 8)
            .Select(day => new StoredBackup(
                BackupService.FileNameFor(Today.AddDays(day)),
                at.AddDays(day)))
            .Append(new StoredBackup("tatil-fotografi.jpg", at.AddYears(-1)))
            .ToArray();

        var deleted = Assert.Single(BackupService.SelectForDeletion(files, 7));
        Assert.Equal(BackupService.FileNameFor(Today), deleted.FileName);
    }

    [Fact]
    public async Task WithoutFolderAccess_NothingIsWritten_AndTheReasonIsReported()
    {
        await WithRoots(async (source, _) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock, hasAccess: false);
            await app.Profiles.CreateAsync("Ayşe");

            Assert.Equal(
                BackupOutcome.NoAccess,
                (await app.Backup.BackUpNowAsync()).Outcome);
            Assert.Empty(app.BackupFiles());
        });
    }

    /// <summary>
    /// Asıl senaryo: uygulama kaldırılır, yedek klasörü kalır. Yeniden kurulan
    /// uygulama yedekleri klasörden kendisi listeler ve en yenisini geri yükler.
    /// </summary>
    [Fact]
    public async Task Reinstall_ListsBackupsFromTheFolder_NewestFirst_AndRestoresOne()
    {
        await WithRoots(async (source, target) =>
        {
            var clock = new MutableClock(Today);
            var folder = Path.Combine(Path.GetDirectoryName(source)!, "Mizan");
            UserProfile ayse;
            await using (var app = Installation.Create(source, clock, folder))
            {
                ayse = await app.Profiles.CreateAsync("Ayşe");
                await app.Profiles.OpenAsync(ayse.Id);
                await app.Backup.BackUpNowAsync();
                clock.Today = Today.AddDays(1);
                var settings = await app.Store.GetSettingsAsync();
                await app.Store.SaveSettingsAsync(settings with { MonthlyLivingBudget = 22_222m });
                await app.Backup.BackUpNowAsync();
            }

            File.WriteAllText(Path.Combine(folder, "notlar.txt"), "başka bir dosya");
            await using var reinstalled = Installation.Create(target, clock, folder);
            var backups = await reinstalled.Backup.ListBackupsAsync();
            Assert.Equal(
                ["Mizan-yedek-2026-09-15.zip", "Mizan-yedek-2026-09-14.zip"],
                backups.Select(backup => backup.FileName));

            await reinstalled.Backup.RestoreAsync(backups[0].FileName);
            await reinstalled.Profiles.OpenAsync(ayse.Id);
            Assert.Equal(22_222m, (await reinstalled.Store.GetSettingsAsync()).MonthlyLivingBudget);
        });
    }

    [Fact]
    public async Task NightlyBackup_WritesAgain_WhenTheUserDeletedTheBackup()
    {
        await WithRoots(async (source, _) =>
        {
            var clock = new MutableClock(Today);
            await using var app = Installation.Create(source, clock);
            var ayse = await app.Profiles.CreateAsync("Ayşe");
            await app.Profiles.OpenAsync(ayse.Id);
            await app.Backup.BackUpIfChangedAsync();

            Directory.Delete(app.BackupFolder, recursive: true);

            Assert.Equal(
                BackupOutcome.Created,
                (await app.Backup.BackUpIfChangedAsync()).Outcome);
            Assert.Single(app.BackupFiles());
        });
    }

    private static MemoryStream Zip(Action<ZipArchive> build)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            build(archive);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Rewrite(
        MemoryStream original,
        Func<ZipArchive, string, bool> replace)
    {
        original.Position = 0;
        using var source = new ZipArchive(original, ZipArchiveMode.Read, leaveOpen: true);
        return Zip(target =>
        {
            foreach (var entry in source.Entries)
            {
                if (replace(target, entry.FullName))
                {
                    continue;
                }

                using var from = entry.Open();
                using var to = target.CreateEntry(entry.FullName).Open();
                from.CopyTo(to);
            }
        });
    }

    private static async Task WithRoots(Func<string, string, Task> test)
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-backup-{Guid.NewGuid():N}");
        var source = Path.Combine(baseDirectory, "source");
        var target = Path.Combine(baseDirectory, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        try
        {
            await test(source, target);
        }
        finally
        {
            // SQLiteAsyncConnection.ResetPool() burada çağrılmamalı: havuz
            // süreç genelidir, paralel koşan diğer testlerin bağlantılarını da
            // kapatır. Store'lar zaten await using ile kapanıyor.
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    private sealed class Installation : IAsyncDisposable
    {
        public required FileSystemProfileRepository Repository { get; init; }
        public required ProfileScopedCoinFlowStore Store { get; init; }
        public required ProfileService Profiles { get; init; }
        public required ProfileBackupArchive Archive { get; init; }
        public required BackupService Backup { get; init; }
        public required string BackupFolder { get; init; }

        public IReadOnlyList<string> BackupFiles() =>
            Directory.Exists(BackupFolder)
                ? Directory.GetFiles(BackupFolder, "*.zip")
                    .Select(path => Path.GetFileName(path))
                    .Order(StringComparer.Ordinal)
                    .ToArray()
                : [];

        /// <param name="backupFolder">
        /// Uygulama klasörünün dışında; iki kurulum aynı klasörü paylaşınca
        /// "kaldırıldı ama yedek kaldı" durumu kurulur.
        /// </param>
        public static Installation Create(
            string root,
            MutableClock clock,
            string? backupFolder = null,
            bool hasAccess = true)
        {
            var repository = new FileSystemProfileRepository(root);
            var store = new ProfileScopedCoinFlowStore(
                id => new SqliteCoinFlowStore(repository.GetDatabasePath(id), true, clock.Today));
            var archive = new ProfileBackupArchive(repository, clock);
            var folder = backupFolder ?? Path.Combine(root, "Mizan");
            var storage = new FolderBackupStorage(folder, "Mizan", new FakeAccess(hasAccess));
            return new Installation
            {
                Repository = repository,
                Store = store,
                Profiles = new ProfileService(repository, store, clock),
                Archive = archive,
                BackupFolder = folder,
                Backup = new BackupService(
                    archive,
                    storage,
                    repository,
                    clock,
                    new BackupOptions(Path.Combine(root, "cache")))
            };
        }

        public ValueTask DisposeAsync() => Store.DisposeAsync();
    }

    private sealed class FakeAccess(bool granted) : IStorageAccess
    {
        public bool HasAccess => granted;

        public Task<bool> RequestAccessAsync() => Task.FromResult(granted);
    }

    // Tarih testin kontrolünde; saat her okumada bir saniye ilerler.
    private sealed class MutableClock(DateOnly today) : IClock
    {
        private DateTimeOffset _tick = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

        public DateOnly Today { get; set; } = today;

        public DateTimeOffset UtcNow
        {
            get
            {
                _tick = _tick.AddSeconds(1);
                return new DateTimeOffset(Today.ToDateTime(TimeOnly.FromTimeSpan(_tick.TimeOfDay)), TimeSpan.Zero);
            }
        }
    }
}
