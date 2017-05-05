﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using ShaderTools.CodeAnalysis.Host;
using ShaderTools.CodeAnalysis.Options;
using ShaderTools.CodeAnalysis.Text;

namespace ShaderTools.CodeAnalysis
{
    public abstract partial class Workspace
    {
        private readonly HostWorkspaceServices _services;

        private readonly SemaphoreSlim _serializationLock = new SemaphoreSlim(initialCount: 1);

        private ImmutableDictionary<DocumentId, Document> _openDocuments = ImmutableDictionary<DocumentId, Document>.Empty;
        private ImmutableDictionary<string, ConfigFile> _configFiles = ImmutableDictionary<string, ConfigFile>.Empty;

        private readonly Dictionary<DocumentId, TextTracker> _textTrackers = new Dictionary<DocumentId, TextTracker>();

        public event EventHandler<DocumentEventArgs> DocumentOpened;
        public event EventHandler<DocumentEventArgs> DocumentClosed;

        public HostWorkspaceServices Services => _services;

        private readonly IWorkspaceTaskScheduler _taskQueue;

        protected Workspace(HostServices host)
        {
            _services = host.CreateWorkspaceServices(this);

            // queue used for sending events
            var workspaceTaskSchedulerFactory = _services.GetRequiredService<IWorkspaceTaskSchedulerFactory>();
            _taskQueue = workspaceTaskSchedulerFactory.CreateEventingTaskQueue();

            _openDocuments = ImmutableDictionary<DocumentId, Document>.Empty;
        }

        public IEnumerable<Document> OpenDocuments
        {
            get
            {
                var latestDocuments = Volatile.Read(ref _openDocuments);
                return latestDocuments.Values;
            }
        }

        public Document GetDocument(DocumentId documentId)
        {
            var latestDocuments = Volatile.Read(ref _openDocuments);
            if (latestDocuments.TryGetValue(documentId, out var document))
                return document;
            return null;
        }

        protected Document CreateDocument(DocumentId documentId, string languageName, SourceText sourceText)
        {
            var languageServices = _services.GetLanguageServices(languageName);
            return new Document(languageServices, documentId, sourceText);
        }

        protected void OnDocumentOpened(Document document, SourceTextContainer textContainer)
        {
            ImmutableInterlocked.AddOrUpdate(ref _openDocuments, document.Id, document, (k, v) => document);

            SignupForTextChanges(document.Id, textContainer, (w, id, text) => w.OnDocumentTextChanged(id, text));

            OnDocumentTextChanged(document);

            DocumentOpened?.Invoke(this, new DocumentEventArgs(document));

            RegisterText(textContainer);
        }

        protected void OnDocumentClosed(DocumentId documentId)
        {
            OnDocumentClosing(documentId);

            ImmutableInterlocked.TryRemove(ref _openDocuments, documentId, out var document);

            // Stop tracking the buffer or update the documentId associated with the buffer.
            if (_textTrackers.TryGetValue(documentId, out var tracker))
            {
                tracker.Disconnect();
                _textTrackers.Remove(documentId);
                
                // No documentIds are attached to this buffer, so stop tracking it.
                this.UnregisterText(tracker.TextContainer);
            }

            DocumentClosed?.Invoke(this, new DocumentEventArgs(document));
        }

        protected void OnDocumentRenamed(DocumentId oldDocumentId, DocumentId newDocumentId)
        {
            if (!ImmutableInterlocked.TryRemove(ref _openDocuments, oldDocumentId, out var oldDocument))
                return;

            ImmutableInterlocked.TryAdd(ref _openDocuments, newDocumentId, oldDocument.WithId(newDocumentId));
        }

        protected Document OnDocumentTextChanged(DocumentId documentId, SourceText newText)
        {
            var newDocument = ImmutableInterlocked.AddOrUpdate(
                ref _openDocuments,
                documentId,
                k => GetDocument(documentId).WithText(newText), // TODO: GetDocument is not thread-safe here?
                (k, v) => v.WithText(newText));

            OnDocumentTextChanged(newDocument);

            return newDocument;
        }

        /// <summary>
        /// Override this method to act immediately when the text of a document has changed, as opposed
        /// to waiting for the corresponding workspace changed event to fire asynchronously.
        /// </summary>
        protected virtual void OnDocumentTextChanged(Document document)
        {
        }

        /// <summary>
        /// Override this method to act immediately when a document is closing, as opposed
        /// to waiting for the corresponding workspace changed event to fire asynchronously.
        /// </summary>
        protected virtual void OnDocumentClosing(DocumentId documentId)
        {
        }

        private void SignupForTextChanges(DocumentId documentId, SourceTextContainer textContainer, Action<Workspace, DocumentId, SourceText> onChangedHandler)
        {
            var tracker = new TextTracker(this, documentId, textContainer, onChangedHandler);
            _textTrackers.Add(documentId, tracker);
            tracker.Connect();
        }

        // TODO: Refactor this.
        public ConfigFile LoadConfigFile(string directory)
        {
            return ImmutableInterlocked.GetOrAdd(
                ref _configFiles, 
                directory.ToLower(), 
                x => ConfigFileLoader.LoadAndMergeConfigFile(x));
        }

        /// <summary>
        /// Executes an action as a background task, as part of a sequential queue of tasks.
        /// </summary>
        protected internal Task ScheduleTask(Action action, string taskName = "Workspace.Task")
        {
            return _taskQueue.ScheduleTask(action, taskName);
        }
    }
}