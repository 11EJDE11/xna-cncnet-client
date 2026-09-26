using System;

namespace ClientLogic.UI;

/// <summary>
/// Runs code on the front end's UI thread. Logic that receives network or timer events on other threads
/// uses this to hand results back to the UI thread, where all lobby state is changed.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="action"/> to run on the UI thread.</summary>
    void Post(Action action);

    /// <summary>Whether the calling thread is the UI thread.</summary>
    bool CheckAccess();
}
