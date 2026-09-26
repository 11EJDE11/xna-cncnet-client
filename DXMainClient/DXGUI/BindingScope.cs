using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;

using ClientLogic.UI;

namespace DTAClient.DXGUI
{
    /// <summary>
    /// Connects observable state to XNA controls for the lifetime of a window or lobby session.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Bind"/> pushes a state property into controls now and whenever it changes.</para>
    /// <para><see cref="OnUserInput"/> forwards a control event to the state, but not while the scope itself is
    /// pushing state into controls: setting a control from code raises the same events as the user changing it,
    /// and forwarding those would echo the state back into itself.</para>
    /// <para>State is only changed on the UI thread. <see cref="Dispose"/> removes every subscription.</para>
    /// </remarks>
    public sealed class BindingScope : IDisposable
    {
        private readonly IUiDispatcher uiDispatcher;
        private readonly List<Action> unsubscribers = [];
        private int pushDepth;

        public BindingScope(IUiDispatcher uiDispatcher)
        {
            this.uiDispatcher = uiDispatcher;
        }

        /// <summary>Whether the scope is currently pushing state into controls.</summary>
        public bool IsPushing => pushDepth > 0;

        /// <summary>
        /// Runs <paramref name="apply"/> now and whenever <paramref name="propertyName"/> of
        /// <paramref name="source"/> changes.
        /// </summary>
        public void Bind(INotifyPropertyChanged source, string propertyName, Action apply)
        {
            Push(apply);

            void Handler(object sender, PropertyChangedEventArgs e)
            {
                // An empty property name means that every property changed
                if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != propertyName)
                    return;

                AssertUiThread();
                Push(apply);
            }

            source.PropertyChanged += Handler;
            unsubscribers.Add(() => source.PropertyChanged -= Handler);
        }

        /// <summary>Runs <paramref name="rebuild"/> now and whenever <paramref name="collection"/> changes.</summary>
        public void BindCollection(INotifyCollectionChanged collection, Action rebuild)
        {
            Push(rebuild);

            void Handler(object sender, NotifyCollectionChangedEventArgs e)
            {
                AssertUiThread();
                Push(rebuild);
            }

            collection.CollectionChanged += Handler;
            unsubscribers.Add(() => collection.CollectionChanged -= Handler);
        }

        /// <summary>
        /// Forwards a control event to <paramref name="handler"/>, except while the scope is pushing state into
        /// controls.
        /// </summary>
        /// <param name="subscribe">Subscribes the given handler to the control's event.</param>
        /// <param name="unsubscribe">Unsubscribes the given handler from the control's event.</param>
        /// <param name="handler">Handles input from the user.</param>
        public void OnUserInput<TEventArgs>(Action<EventHandler<TEventArgs>> subscribe,
            Action<EventHandler<TEventArgs>> unsubscribe, EventHandler<TEventArgs> handler)
        {
            void Guarded(object sender, TEventArgs e)
            {
                if (!IsPushing)
                    handler(sender, e);
            }

            subscribe(Guarded);
            unsubscribers.Add(() => unsubscribe(Guarded));
        }

        /// <inheritdoc cref="OnUserInput{TEventArgs}"/>
        public void OnUserInput(Action<EventHandler> subscribe, Action<EventHandler> unsubscribe, EventHandler handler)
        {
            void Guarded(object sender, EventArgs e)
            {
                if (!IsPushing)
                    handler(sender, e);
            }

            subscribe(Guarded);
            unsubscribers.Add(() => unsubscribe(Guarded));
        }

        public void Dispose()
        {
            for (int i = unsubscribers.Count - 1; i >= 0; i--)
                unsubscribers[i]();

            unsubscribers.Clear();
        }

        private void Push(Action apply)
        {
            pushDepth++;
            try
            {
                apply();
            }
            finally
            {
                pushDepth--;
            }
        }

        [Conditional("DEBUG")]
        private void AssertUiThread() =>
            Debug.Assert(uiDispatcher.CheckAccess(), "Bound state must only change on the UI thread.");
    }
}
