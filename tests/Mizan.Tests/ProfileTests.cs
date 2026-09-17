using System.Reflection;
using System.Runtime.CompilerServices;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Infrastructure.Persistence;

namespace Mizan.Tests;

/// <summary>
/// Profil = ayrı veritabanı. Asıl sözleşme: yeni profil uygulama yeni
/// yüklenmiş gibi davranır ve bir profilde yapılan hiçbir şey diğerine
/// dokunmaz. Profil öncesi sürümün verisi ilk açılışta kaybolmaz.
/// </summary>
public sealed class ProfileTests
{
    private static readonly DateOnly Today = new(2026, 8, 20);

    [Fact]
    public async Task NewProfile_StartsLikeAFreshInstall_AndLeavesTheOtherUntouched()
    {
        await WithProfiles(async (profiles, store) =>
        {
            var ayse = await profiles.CreateAsync("Ayşe");
            var mehmet = await profiles.CreateAsync("Mehmet");
            var service = TestFactory.Service(store, Today);

            await profiles.OpenAsync(ayse.Id);
            await service.LoadCanonicalDevelopmentDataAsync();
            Assert.False(await service.IsOnboardingRequiredAsync());
            var ayseSalaries = (await service.GetFinancialPlanAsync()).Salaries.Count;
            Assert.True(ayseSalaries > 0);

            await profiles.OpenAsync(mehmet.Id);
            Assert.True(await service.IsOnboardingRequiredAsync());
            var mehmetPlan = await service.GetFinancialPlanAsync();
            Assert.Empty(mehmetPlan.Salaries);
            Assert.Empty(mehmetPlan.Loans);
            Assert.Empty(mehmetPlan.CreditCards);

            // Mehmet'in profilinde silmek Ayşe'nin verisine ulaşmaz.
            await service.ClearDevelopmentDataAsync();

            await profiles.OpenAsync(ayse.Id);
            Assert.False(await service.IsOnboardingRequiredAsync());
            Assert.Equal(
                ayseSalaries,
                (await service.GetFinancialPlanAsync()).Salaries.Count);
        });
    }

    [Fact]
    public async Task ClosedProfile_RejectsEveryCall_InsteadOfWritingSomewhereElse()
    {
        await WithProfiles(async (profiles, store) =>
        {
            var profile = await profiles.CreateAsync("Ayşe");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.GetSettingsAsync());

            await profiles.OpenAsync(profile.Id);
            await store.GetSettingsAsync();

            await profiles.CloseAsync();
            Assert.Null(profiles.ActiveProfile);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.GetSettingsAsync());
        });
    }

    [Fact]
    public async Task ExistingDatabase_BecomesTheFirstProfile_WithItsData()
    {
        await WithRoot(async root =>
        {
            var legacyPath = Path.Combine(
                root,
                FileSystemProfileRepository.DatabaseFileName);
            await using (var legacy = new SqliteMizanStore(legacyPath, true, Today))
            {
                await TestFactory.Service(legacy, Today)
                    .LoadCanonicalDevelopmentDataAsync();
            }

            var (profiles, store, repository) = Create(root);
            try
            {
                var list = await profiles.GetProfilesAsync();

                var adopted = Assert.Single(list);
                Assert.Equal(UserProfile.DefaultName, adopted.Name);
                Assert.False(File.Exists(legacyPath));
                Assert.False(repository.HasLegacyDatabase);
                Assert.True(File.Exists(repository.GetDatabasePath(adopted.Id)));

                await profiles.OpenAsync(adopted.Id);
                var service = TestFactory.Service(store, Today);
                Assert.False(await service.IsOnboardingRequiredAsync());
                Assert.Equal(30_000m, (await store.GetSettingsAsync()).MonthlyVariableExpenseAllowance);

                // İkinci açılışta tekrar taşınacak bir şey yok.
                await profiles.CloseAsync();
                Assert.Single(await profiles.GetProfilesAsync());
            }
            finally
            {
                await store.DisposeAsync();
            }
        });
    }

    [Fact]
    public async Task ProfileWhoseMetadataIsLost_IsStillListed()
    {
        await WithProfiles(async (profiles, store) =>
        {
            var profile = await profiles.CreateAsync("Ayşe");
            await profiles.OpenAsync(profile.Id);
            await profiles.CloseAsync();

            var directory = Path.GetDirectoryName(
                RepositoryOf(profiles).GetDatabasePath(profile.Id))!;
            File.WriteAllText(Path.Combine(directory, "profile.json"), "{bozuk");

            var recovered = Assert.Single(await profiles.GetProfilesAsync());
            Assert.Equal(profile.Id, recovered.Id);
            Assert.Equal(UserProfile.DefaultName, recovered.Name);
        });
    }

    [Fact]
    public async Task ProfileNames_AreTrimmedRequiredShortAndUnique()
    {
        await WithProfiles(async (profiles, _) =>
        {
            var ipek = await profiles.CreateAsync("  İpek  ");
            Assert.Equal("İpek", ipek.Name);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => profiles.CreateAsync("   "));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => profiles.CreateAsync(new string('a', UserProfile.MaxNameLength + 1)));
            // Türkçe büyük/küçük harf: "ipek" ile "İpek" aynı ad.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => profiles.CreateAsync("ipek"));

            var renamed = await profiles.RenameAsync(ipek.Id, "İPEK");
            Assert.Equal("İPEK", renamed.Name);
            Assert.Equal("İPEK", Assert.Single(await profiles.GetProfilesAsync()).Name);
        });
    }

    [Fact]
    public async Task Delete_RemovesTheData_ButNeverTheLastOrTheOpenProfile()
    {
        await WithProfiles(async (profiles, _) =>
        {
            var ayse = await profiles.CreateAsync("Ayşe");
            var mehmet = await profiles.CreateAsync("Mehmet");
            await profiles.OpenAsync(mehmet.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => profiles.DeleteAsync(mehmet.Id));

            await profiles.CloseAsync();
            await profiles.DeleteAsync(mehmet.Id);
            var databasePath = RepositoryOf(profiles).GetDatabasePath(mehmet.Id);
            Assert.False(Directory.Exists(Path.GetDirectoryName(databasePath)));
            Assert.Equal(ayse.Id, Assert.Single(await profiles.GetProfilesAsync()).Id);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => profiles.DeleteAsync(ayse.Id));
        });
    }

    [Fact]
    public async Task Profiles_AreListedInCreationOrder_AndOpeningStartsANewSession()
    {
        await WithProfiles(async (profiles, _) =>
        {
            var first = await profiles.CreateAsync("Birinci");
            var second = await profiles.CreateAsync("İkinci");

            await profiles.OpenAsync(second.Id);
            var session = profiles.SessionId;
            await profiles.OpenAsync(second.Id);

            Assert.NotEqual(session, profiles.SessionId);
            Assert.NotNull(profiles.ActiveProfile!.LastOpenedAt);
            Assert.Equal(
                [first.Id, second.Id],
                (await profiles.GetProfilesAsync()).Select(profile => profile.Id));
        });
    }

    /// <summary>
    /// Sarmalayıcı elle yazılmış 40 küsur iletimden oluşuyor; birinin yanlış
    /// metoda ya da yanlış argümanla gitmesi derlemeyi kırmaz. Her arayüz
    /// metodunu çağırıp alttaki store'a birebir aynı çağrının ulaştığını ölçer.
    /// </summary>
    [Fact]
    public async Task ScopedStore_ForwardsEveryCallUnchanged()
    {
        var inner = DispatchProxy.Create<IMizanStore, RecordingStore>();
        var recorder = (RecordingStore)(object)inner;
        var scoped = new ProfileScopedMizanStore(_ => inner);
        await scoped.OpenAsync(Guid.NewGuid());

        foreach (var method in typeof(IMizanStore).GetMethods())
        {
            recorder.Calls.Clear();
            var arguments = method.GetParameters()
                .Select(parameter => SentinelFor(parameter.ParameterType))
                .ToArray();

            await (Task)method.Invoke(scoped, arguments)!;

            var call = Assert.Single(recorder.Calls);
            Assert.Equal(method, call.Method);
            Assert.Equal(arguments.Length, call.Arguments.Length);
            for (var index = 0; index < arguments.Length; index++)
            {
                if (arguments[index] is null || arguments[index]!.GetType().IsValueType)
                {
                    Assert.Equal(arguments[index], call.Arguments[index]);
                }
                else
                {
                    Assert.Same(arguments[index], call.Arguments[index]);
                }
            }
        }
    }

    private static object? SentinelFor(Type type)
    {
        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (type == typeof(CancellationToken))
        {
            return new CancellationTokenSource().Token;
        }

        if (type == typeof(string))
        {
            // Aynı referansın iletildiğini ölçmek için yeni bir örnek.
            return new string('k', 3);
        }

        if (type.IsInterface && type.IsGenericType)
        {
            // IReadOnlyList<T> gibi koleksiyon parametreleri: yeni bir dizi.
            return Array.CreateInstance(type.GetGenericArguments()[0], 1);
        }

        return type.IsValueType
            ? Activator.CreateInstance(type)
            : RuntimeHelpers.GetUninitializedObject(type);
    }

    public class RecordingStore : DispatchProxy
    {
        public List<(MethodInfo Method, object?[] Arguments)> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add((targetMethod!, args ?? []));
            var returnType = targetMethod!.ReturnType;
            if (!returnType.IsGenericType)
            {
                return Task.CompletedTask;
            }

            var resultType = returnType.GetGenericArguments()[0];
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [null]);
        }
    }

    private static readonly ConditionalWeakTable<ProfileService, FileSystemProfileRepository>
        Repositories = new();

    private static FileSystemProfileRepository RepositoryOf(ProfileService profiles) =>
        Repositories.TryGetValue(profiles, out var repository)
            ? repository
            : throw new InvalidOperationException();

    private static (ProfileService Profiles, ProfileScopedMizanStore Store, FileSystemProfileRepository Repository)
        Create(string root)
    {
        var repository = new FileSystemProfileRepository(root);
        var store = new ProfileScopedMizanStore(
            id => new SqliteMizanStore(repository.GetDatabasePath(id), true, Today));
        var profiles = new ProfileService(repository, store, new SteppingClock());
        Repositories.Add(profiles, repository);
        return (profiles, store, repository);
    }

    private static Task WithProfiles(
        Func<ProfileService, ProfileScopedMizanStore, Task> test) =>
        WithRoot(async root =>
        {
            var (profiles, store, _) = Create(root);
            try
            {
                await test(profiles, store);
            }
            finally
            {
                await store.DisposeAsync();
            }
        });

    private static async Task WithRoot(Func<string, Task> test)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-profiles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await test(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // Her okumada bir saniye ilerler: oluşturulma sırası ve "son açılış"
    // eşit zaman damgalarına takılmadan ölçülebilsin.
    private sealed class SteppingClock : IClock
    {
        private DateTimeOffset _now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

        public DateOnly Today => DateOnly.FromDateTime(_now.Date);

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }
}
