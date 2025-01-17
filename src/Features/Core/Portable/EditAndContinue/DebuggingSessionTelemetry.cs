﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Internal.Log;

namespace Microsoft.CodeAnalysis.EditAndContinue
{
    internal sealed class DebuggingSessionTelemetry
    {
        internal readonly struct Data
        {
            public readonly ImmutableArray<EditSessionTelemetry.Data> EditSessionData;
            public readonly int EmptyEditSessionCount;
            public readonly int EmptyHotReloadEditSessionCount;

            public Data(DebuggingSessionTelemetry telemetry)
            {
                EditSessionData = telemetry._editSessionData.ToImmutableArray();
                EmptyEditSessionCount = telemetry._emptyEditSessionCount;
                EmptyHotReloadEditSessionCount = telemetry._emptyHotReloadEditSessionCount;
            }
        }

        private readonly object _guard = new();

        private readonly List<EditSessionTelemetry.Data> _editSessionData = new();
        private int _emptyEditSessionCount;
        private int _emptyHotReloadEditSessionCount;

        public Data GetDataAndClear()
        {
            lock (_guard)
            {
                var data = new Data(this);
                _editSessionData.Clear();
                _emptyEditSessionCount = 0;
                _emptyHotReloadEditSessionCount = 0;
                return data;
            }
        }

        public void LogEditSession(EditSessionTelemetry.Data editSessionTelemetryData)
        {
            lock (_guard)
            {
                if (editSessionTelemetryData.IsEmpty)
                {
                    if (editSessionTelemetryData.InBreakState)
                        _emptyEditSessionCount++;
                    else
                        _emptyHotReloadEditSessionCount++;
                }
                else
                {
                    _editSessionData.Add(editSessionTelemetryData);
                }
            }
        }

        // Example query:
        //
        // RawEventsVS
        // | where EventName == "vs/ide/vbcs/debugging/encsession/editsession"
        // | project EventId, EventName, Properties, Measures, MacAddressHash
        // | where Measures["vs.ide.vbcs.debugging.encsession.editsession.emitdeltaerroridcount"] == 0
        // | extend HasValidChanges = Properties["vs.ide.vbcs.debugging.encsession.editsession.hadvalidchanges"] == "True"
        // | where HasValidChanges
        // | extend IsHotReload = Properties["vs.ide.vbcs.debugging.encsession.editsession.inbreakstate"] == "False"
        // | extend IsEnC = not(IsHotReload)
        // | summarize HotReloadUsers = dcountif(MacAddressHash, IsHotReload),
        //             EncUsers = dcountif(MacAddressHash, IsEnC)
        public static void Log(Data data, Action<FunctionId, LogMessage> log, Func<int> getNextId)
        {
            const string SessionId = nameof(SessionId);
            const string EditSessionId = nameof(EditSessionId);

            var debugSessionId = getNextId();

            log(FunctionId.Debugging_EncSession, KeyValueLogMessage.Create(map =>
            {
                map[SessionId] = debugSessionId;
                map["SessionCount"] = data.EditSessionData.Count(session => session.InBreakState);
                map["EmptySessionCount"] = data.EmptyEditSessionCount;
                map["HotReloadSessionCount"] = data.EditSessionData.Count(session => !session.InBreakState);
                map["EmptyHotReloadSessionCount"] = data.EmptyHotReloadEditSessionCount;
            }));

            foreach (var editSessionData in data.EditSessionData)
            {
                var editSessionId = getNextId();

                log(FunctionId.Debugging_EncSession_EditSession, KeyValueLogMessage.Create(map =>
                {
                    map[SessionId] = debugSessionId;
                    map[EditSessionId] = editSessionId;

                    map["HadCompilationErrors"] = editSessionData.HadCompilationErrors;
                    map["HadRudeEdits"] = editSessionData.HadRudeEdits;
                    map["HadValidChanges"] = editSessionData.HadValidChanges;
                    map["HadValidInsignificantChanges"] = editSessionData.HadValidInsignificantChanges;

                    map["RudeEditsCount"] = editSessionData.RudeEdits.Length;
                    map["EmitDeltaErrorIdCount"] = editSessionData.EmitErrorIds.Length;
                    map["InBreakState"] = editSessionData.InBreakState;
                    map["Capabilities"] = (int)editSessionData.Capabilities;
                }));

                foreach (var errorId in editSessionData.EmitErrorIds)
                {
                    log(FunctionId.Debugging_EncSession_EditSession_EmitDeltaErrorId, KeyValueLogMessage.Create(map =>
                    {
                        map[SessionId] = debugSessionId;
                        map[EditSessionId] = editSessionId;
                        map["ErrorId"] = errorId;
                    }));
                }

                foreach (var (editKind, syntaxKind) in editSessionData.RudeEdits)
                {
                    log(FunctionId.Debugging_EncSession_EditSession_RudeEdit, KeyValueLogMessage.Create(map =>
                    {
                        map[SessionId] = debugSessionId;
                        map[EditSessionId] = editSessionId;

                        map["RudeEditKind"] = editKind;
                        map["RudeEditSyntaxKind"] = syntaxKind;
                        map["RudeEditBlocking"] = editSessionData.HadRudeEdits;
                    }));
                }
            }
        }
    }
}
