using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Storage;

public sealed class AccountStore
{
    private const bool ProtectAccountFile = false;

    public async Task<IReadOnlyList<AccountProfile>> LoadAsync()
    {
        var list = await JsonFileStore.LoadAsync(
            AppPaths.AccountsFile,
            new List<AccountProfile>(),
            ProtectAccountFile).ConfigureAwait(false);

        return list;
    }

    public Task SaveAsync(IEnumerable<AccountProfile> accounts)
    {
        return JsonFileStore.SaveAsync(
            AppPaths.AccountsFile,
            accounts.ToList(),
            ProtectAccountFile);
    }
}
