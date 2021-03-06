﻿//-----------------------------------------------------------------------------
// Filename: SIPDialPlanManager.cs
//
// Description: This class loads and compiles dynamic dialplans. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
// 09 Jan 2021  Aaron Clauson   Load dialplan from database instead of file system.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using signalrtc.DataAccess;

namespace signalrtc
{
    /// <summary>
    /// Loads a dialplan for the B2B user agent core from a database. The dialplan must be valid C#
    /// source and is compiled with Roslyn and executed for each incoming call.
    /// </summary>
    /// <example>
    /// var inUri = uasTx.TransactionRequestURI;
    /// 
    /// switch (inUri.User)
    /// {
    ///     case "123":
    ///         //return new SIPCallDescriptor("time@sipsorcery.com", uasTx.TransactionRequest.Body);
    ///         return new SIPCallDescriptor("aaron@192.168.0.50:6060", uasTx.TransactionRequest.Body);
    ///     case "456":
    ///         return new SIPCallDescriptor("idontexist@sipsorcery.com", uasTx.TransactionRequest.Body);
    ///     default:
    ///         return null;
    /// }
    /// </example>
    public class SIPDialPlanManager
    {
        public const string DEFAULT_DIALPLAN_NAME = "default";
        public const string DIAL_PLAN_CODE_TEMPLATE =
@"
using fwd = SIPSorcery.SIP.App.SIPCallDescriptor;

public static class DialPlanScript
{{
    public static SIPCallDescriptor Lookup(UASInviteTransaction uasTx, ISIPAccount from)
    {{
        {0}
    }}
}}";
        private readonly ILogger _logger = SIPSorcery.LogFactory.CreateLogger<SIPDialPlanManager>();

        private DateTime _dialplanLastUpdated = DateTime.MinValue;
        private SIPDialPlanDataLayer _sipDialPlanDataLayer;
        private ScriptRunner<Object> _dialPlanScriptRunner;

        public SIPDialPlanManager(IDbContextFactory<SIPAssetsDbContext> dbContextFactory)
        {
            _sipDialPlanDataLayer = new SIPDialPlanDataLayer(dbContextFactory);
        }

        public async Task<SIPDialPlan> LoadDialPlan()
        {
            var dialplan = await _sipDialPlanDataLayer.Get(x => x.DialPlanName == DEFAULT_DIALPLAN_NAME);

            if (dialplan == null)
            {
                _logger.LogError($"SIP DialPlan Manager could not load the default dialplan. Ensure a dialplan with the name of \"{DEFAULT_DIALPLAN_NAME}\" exists.");
            }

            return dialplan;
        }

        public string CompileDialPlan(string dialplanScript, DateTime lastUpdated)
        {
            try
            {
                string dialPlanClass = string.Format(DIAL_PLAN_CODE_TEMPLATE, dialplanScript);

                _logger.LogDebug($"Compiling dialplan...");

                DateTime startTime = DateTime.Now;

                _dialPlanScriptRunner = CSharpScript.Create(dialPlanClass,
                   ScriptOptions.Default
                   .WithImports("System", "SIPSorcery.SIP", "SIPSorcery.SIP.App")
                   .AddReferences(typeof(SIPSorcery.SIP.App.SIPCallDescriptor).GetTypeInfo().Assembly),
                   typeof(SIPDialPlanGlobals))
                   .ContinueWith("DialPlanScript.Lookup(UasTx, From)")
                   .CreateDelegate();

                // Script compilations chew chunks of memory (approx 256MB for small dialplans). Not an issue for a dev machine but
                // kills the Azure B1s virtual machines. By immediately collecting the memory from any previous script compilations 
                // the intent is the memory usage spikes stay as low as possible.
                GC.Collect();

                var duration = DateTime.Now.Subtract(startTime);
                _logger.LogInformation($"SIP DialPlan Manager successfully compiled dialplan in {duration.TotalMilliseconds:0.##}ms.");

                _dialplanLastUpdated = lastUpdated;

                return null;
            }
            catch (Exception excp)
            {
                _logger.LogError($"SIP DialPlan Manager failed to compile dialplan. {excp.Message}");
                return excp.Message;
            }
        }

        /// <summary>
        /// This function type is to allow B2B user agents to lookup the forwarding destination
        /// for an accepted User Agent Server (UAS) call leg. The intent is that functions
        /// can implement a form of a dialplan and pass to the B2BUA core.
        /// </summary>
        /// <param name="uas">A User Agent Server (UAS) transaction that has been accepted
        /// for forwarding.</param>
        /// <returns>A call descriptor for the User Agent Client (UAC) call leg that will
        /// be bridged to the UAS leg.</returns>
        public async Task<SIPCallDescriptor> Lookup(UASInviteTransaction uasTx, ISIPAccount from)
        {
            var dialplan = await LoadDialPlan();

            //_logger.LogDebug($"Our dialplan last update {TrimMilliseconds(_dialplanLastUpdated).ToString("o")}, " +
            //    $"database last update {TrimMilliseconds(dialplan.LastUpdate).ToString("o")}.");

            if (dialplan != null && TrimMilliseconds(DateTime.Parse(dialplan.LastUpdate)) > TrimMilliseconds(_dialplanLastUpdated))
            {
                _logger.LogInformation($"SIP DialPlan Manager loading updated dialplan.");
                CompileDialPlan(dialplan.DialPlanScript, DateTime.Parse(dialplan.LastUpdate));
            }

            if (_dialPlanScriptRunner != null)
            {
                var result = await _dialPlanScriptRunner.Invoke(new SIPDialPlanGlobals { UasTx = uasTx, From = from });
                return result as SIPCallDescriptor;
            }
            else
            {
                return null;
            }
        }

        public async Task UpdateDialPlanScript(string dialPlanScript, DateTime lastUpdate)
        {
            await _sipDialPlanDataLayer.UpdateDialPlanScript(dialPlanScript, lastUpdate);
        }

        public static DateTime TrimMilliseconds(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, 0, dt.Kind);
        }
    }
}
