﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using ShaderTools.CodeAnalysis.Text.Properties;

namespace ShaderTools.CodeAnalysis.Text
{
    public static partial class Extensions
    {
        public static SourceTextContainer AsTextContainer(this ITextBuffer buffer)
            => TextBufferContainer.From(buffer);

        public static ITextBuffer GetTextBuffer(this SourceTextContainer textContainer)
            => TryGetTextBuffer(textContainer) ?? throw new ArgumentException(TextEditorResources.textContainer_is_not_a_SourceTextContainer_that_was_created_from_an_ITextBuffer, nameof(textContainer));

        public static ITextBuffer TryGetTextBuffer(this SourceTextContainer textContainer)
            => (textContainer as TextBufferContainer)?.EditorTextBuffer;

        /// <summary>
        /// Returns the ITextSnapshot behind this SourceText, or null if it wasn't created from one.
        /// 
        /// Note that multiple ITextSnapshots may map to the same SourceText instance if
        /// ITextSnapshot.Version.ReiteratedVersionNumber doesn't change.
        /// </summary>
        /// <returns>The underlying ITextSnapshot.</returns>
        public static ITextSnapshot FindCorrespondingEditorTextSnapshot(this SourceText text)
            => (text as SnapshotSourceText)?.EditorSnapshot;

        internal static TextLine AsTextLine(this ITextSnapshotLine line)
            => line.Snapshot.AsText().Lines[line.LineNumber];

        public static SourceText AsText(this ITextSnapshot textSnapshot)
            => SnapshotSourceText.From(textSnapshot);

        internal static SourceText AsRoslynText(this ITextSnapshot textSnapshot)
            => new SnapshotSourceText.ClosedSnapshotSourceText(textSnapshot);

        /// <summary>
        /// Gets the workspace corresponding to the text buffer.
        /// </summary>
        public static Workspace GetWorkspace(this ITextBuffer buffer)
        {
            var container = buffer.AsTextContainer();
            if (Workspace.TryGetWorkspace(container, out var workspace))
            {
                return workspace;
            }

            return null;
        }

        /// <summary>
        /// Gets the <see cref="Document"/> from the corresponding <see cref="Workspace.CurrentSolution"/> that is associated with the <see cref="ITextSnapshot"/>'s buffer
        /// in its current project context, updated to contain the same text as the snapshot if necessary. There may be multiple <see cref="Document"/>s
        /// associated with the buffer if it is linked into multiple projects or is part of a Shared Project. In this case, the <see cref="Workspace"/>
        /// is responsible for keeping track of which of these <see cref="Document"/>s is in the current project context.
        /// </summary>
        public static Document GetOpenDocumentInCurrentContextWithChanges(this ITextSnapshot text)
            => text.AsText().GetOpenDocumentInCurrentContextWithChanges();

        /// <summary>
        /// Tries to get the document corresponding to the text from the current partial solution 
        /// associated with the text's container. If the document does not contain the exact text a document 
        /// from a new solution containing the specified text is constructed. If no document is associated
        /// with the specified text's container, or the text's container isn't associated with a workspace,
        /// then the method returns false.
        /// </summary>
        internal static Task<Document> GetDocumentWithFrozenPartialSemanticsAsync(this SourceText text, CancellationToken cancellationToken)
        {
            var document = text.GetOpenDocumentInCurrentContextWithChanges();

            //if (document != null)
            //{
            //    return await document.WithFrozenPartialSemanticsAsync(cancellationToken).ConfigureAwait(false);
            //}
            return Task.FromResult(document);

            //return null;
        }

        //internal static bool CanApplyChangeDocumentToWorkspace(this ITextBuffer buffer)
        //    => Workspace.TryGetWorkspace(buffer.AsTextContainer(), out var workspace) &&
        //       workspace.CanApplyChange(ApplyChangesKind.ChangeDocument);

        /// <summary>
        /// Get the encoding used to load this <see cref="ITextBuffer"/> if possible.
        /// <para>
        /// Note that this will return <see cref="Encoding.UTF8"/> if the <see cref="ITextBuffer"/>
        /// didn't come from an <see cref="ITextDocument"/>, or if the <see cref="ITextDocument"/>
        /// is already closed.
        /// </para>
        /// </summary>
        internal static Encoding GetEncodingOrUTF8(this ITextBuffer textBuffer)
            => textBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDocument)
                ? textDocument.Encoding
                : Encoding.UTF8;

        internal static string GetFilePath(this ITextBuffer textBuffer)
            => textBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDocument)
                ? textDocument.FilePath
                : null;
    }
}