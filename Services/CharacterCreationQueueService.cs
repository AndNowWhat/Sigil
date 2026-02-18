using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sigil.Models;

namespace Sigil.Services;

internal sealed record AccountCreationBatch(
    string AccountId,
    string DisplayName,
    string RsSessionToken,
    int ToCreate,
    int DelaySeconds);

public sealed class CharacterCreationQueueService : IDisposable
{
    private readonly JagexAccountService _jagexService;
    private readonly Channel<AccountCreationBatch> _channel =
        Channel.CreateUnbounded<AccountCreationBatch>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<string> _queuedAccountIds = new();
    private CancellationTokenSource _cts = new();
    private Task? _worker;

    // UI notification events (subscribers must dispatch to UI thread themselves)
    public event Action<string, IReadOnlyList<GameAccount>>? CharacterCreated;  // accountId, updatedList
    public event Action<string, int, int>? BatchCompleted;                      // accountId, created, skipped
    public event Action<int>? PendingCountChanged;                              // remaining batch count
    public event Action<string>? StatusUpdated;                                 // status message text

    public int PendingCount { get; private set; }
    public bool IsActive => _worker != null && !_worker.IsCompleted;

    public CharacterCreationQueueService(JagexAccountService jagexService)
    {
        _jagexService = jagexService;
    }

    /// <summary>
    /// Queues auto-creation for the given account. Returns false if already queued or already at max.
    /// </summary>
    public Task<bool> EnqueueAsync(AccountProfile account, string rsSessionToken, int delaySeconds = 60, CancellationToken ct = default)
    {
        if (_queuedAccountIds.Contains(account.AccountId))
        {
            StatusUpdated?.Invoke($"{account.DisplayName} is already in the queue.");
            return Task.FromResult(false);
        }

        int toCreate = 20 - account.GameAccounts.Count;
        if (toCreate <= 0)
        {
            StatusUpdated?.Invoke($"{account.DisplayName} already has 20 characters.");
            return Task.FromResult(false);
        }

        _queuedAccountIds.Add(account.AccountId);
        PendingCount++;
        PendingCountChanged?.Invoke(PendingCount);

        _channel.Writer.TryWrite(
            new AccountCreationBatch(account.AccountId, account.DisplayName, rsSessionToken, toCreate, delaySeconds));

        EnsureWorker();
        return Task.FromResult(true);
    }

    private void EnsureWorker()
    {
        if (_worker == null || _worker.IsCompleted)
        {
            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => ProcessQueueAsync(_cts.Token));
        }
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var batch in _channel.Reader.ReadAllAsync(ct))
        {
            await ProcessBatchAsync(batch, ct);
            PendingCount--;
            PendingCountChanged?.Invoke(PendingCount);
            _queuedAccountIds.Remove(batch.AccountId);
        }
    }

    private async Task ProcessBatchAsync(AccountCreationBatch batch, CancellationToken ct)
    {
        int created = 0, skipped = 0;
        for (int i = 0; i < batch.ToCreate && !ct.IsCancellationRequested; i++)
        {
            bool success = false;
            for (int attempt = 1; attempt <= 5 && !success && !ct.IsCancellationRequested; attempt++)
            {
                StatusUpdated?.Invoke(
                    $"[{batch.DisplayName}] Creating character {i + 1}/{batch.ToCreate}" +
                    (attempt > 1 ? $" (retry {attempt}/5)" : ""));
                try
                {
                    var updated = await _jagexService.CreateGameAccountAsync(batch.RsSessionToken, ct);
                    CharacterCreated?.Invoke(batch.AccountId, updated);
                    created++;
                    success = true;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    bool isRateLimit = ex.Message.Contains("409") ||
                                      ex.Message.Contains("TOO_MANY_ACCOUNTS", StringComparison.OrdinalIgnoreCase);
                    if (attempt < 5)
                    {
                        var wait = isRateLimit ? batch.DelaySeconds * 2 : batch.DelaySeconds;
                        StatusUpdated?.Invoke(
                            $"[{batch.DisplayName}] {(isRateLimit ? "Rate limited" : "Failed")} (attempt {attempt}/5), retrying in {wait}s...");
                        await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false);
                    }
                    else
                    {
                        skipped++;
                        StatusUpdated?.Invoke(
                            $"[{batch.DisplayName}] Skipped after 5 failures: {ex.Message}");
                    }
                }
            }

            // Cool-down after each successful creation before the next one
            if (success && (i < batch.ToCreate - 1 || PendingCount > 0))
                await Task.Delay(TimeSpan.FromSeconds(batch.DelaySeconds), ct).ConfigureAwait(false);
        }
        BatchCompleted?.Invoke(batch.AccountId, created, skipped);
    }

    /// <summary>
    /// Cancels all pending and in-progress work and drains the channel.
    /// </summary>
    public void CancelAll()
    {
        _cts.Cancel();
        // Drain remaining items from the channel
        while (_channel.Reader.TryRead(out _)) { }
        _queuedAccountIds.Clear();
        PendingCount = 0;
        PendingCountChanged?.Invoke(0);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
