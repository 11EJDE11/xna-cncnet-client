#nullable enable
using System;


namespace DTAClient.Domain.Multiplayer;

public interface ICacheManager<TInput, TOutput> : IDisposable
{
    /// <summary>
    /// Gets the number of elements contained in the collection.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Clears all cached items. Subclasses may override <see cref="CacheManagerBase{TInput,TOutput}.OnOutputRemoved"/> to release resources on eviction.
    /// </summary>
    public void Clear();

    /// <summary>
    /// Requests an output to be computed for the specified input.
    /// The returned output is owned by the cache and may be evicted and disposed at any time.
    /// Callers must use the output immediately and must not retain a reference to it.
    /// </summary>
    /// <param name="input">The input to get the output.</param>
    /// <param name="output">The cached output if found or computed.</param>
    /// <param name="syncComputeOnCacheMiss">If true, the method will attempt to compute the output immediately if it's not cached, which may be CPU-intensive. If false, the input will be queued for asynchronous processing if <see cref="addToQueue"/> holds.</param>
    /// <param name="addToQueue">This parameter is ignored if <see cref="syncComputeOnCacheMiss"/> is true. Otherwise, if true, the input will be added to the processing queue if not already cached; if false, the method will simply return null on cache miss without queuing.</param>
    /// <returns>True if the output was found in cache or computed synchronously; false if the output is not available yet.</returns>
    public bool Request(TInput input, out TOutput? output, bool syncComputeOnCacheMiss = false, bool addToQueue = true);
}