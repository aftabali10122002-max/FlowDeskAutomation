namespace FlowDisk.Api.Attachments;

public sealed class AttachmentUploadConcurrencyLimiter(
    RequestAttachmentOptions options)
{
    private readonly object sync = new();
    private readonly Dictionary<int, UserSlot> userSlots = [];

    public IDisposable? TryAcquire(int userId)
    {
        lock (sync)
        {
            if (!userSlots.TryGetValue(userId, out var slot))
            {
                slot = new UserSlot(new SemaphoreSlim(
                    options.MaximumConcurrentUploadsPerUser,
                    options.MaximumConcurrentUploadsPerUser));
                userSlots.Add(userId, slot);
            }

            if (!slot.Semaphore.Wait(0))
            {
                return null;
            }

            slot.ActiveLeases++;
            return new SemaphoreLease(this, userId, slot);
        }
    }

    private void Release(int userId, UserSlot slot)
    {
        lock (sync)
        {
            slot.Semaphore.Release();
            slot.ActiveLeases--;
            if (slot.ActiveLeases == 0)
            {
                userSlots.Remove(userId);
                slot.Semaphore.Dispose();
            }
        }
    }

    private sealed class UserSlot(SemaphoreSlim semaphore)
    {
        public SemaphoreSlim Semaphore { get; } = semaphore;
        public int ActiveLeases { get; set; }
    }

    private sealed class SemaphoreLease(
        AttachmentUploadConcurrencyLimiter owner,
        int userId,
        UserSlot slot) : IDisposable
    {
        private AttachmentUploadConcurrencyLimiter? currentOwner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref currentOwner, null)?.Release(
                userId,
                slot);
        }
    }
}
