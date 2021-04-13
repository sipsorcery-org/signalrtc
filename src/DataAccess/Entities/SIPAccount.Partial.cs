// ============================================================================
// FileName: SIPAccount.Partial.cs
//
// Description:
// Represents the SIPAccount entity. This partial class is used to apply 
// additional properties or metadata to the auto generated SIPAccount class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 31 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc;
using signalrtc.Models;
using SIPSorcery.SIP.App;

#nullable disable

namespace signalrtc.DataAccess
{
    [ModelMetadataType(typeof(SIPAccountMetadata))]
    public partial class SIPAccount
    {
        public string SIPDomain => Domain?.Domain;

        /// <summary>
        /// Address of Record. Convenience property.
        /// </summary>
        [Display(Name = "SIP Account Address")]
        public string AOR => SIPUsername + "@" + Domain?.Domain;

        [NotMapped]
        public string SIPPassword { get; set; }

        public ISIPAccount ToSIPAccountModel()
        {
            return new SIPAccountModel
            {
                ID = !string.IsNullOrEmpty(ID) ? new Guid(ID) : Guid.Empty,
                SIPUsername = SIPUsername,
                HA1Digest = HA1Digest,
                SIPDomain = SIPDomain,
                IsDisabled = IsDisabled >= 1
            };
        }
    }

    public class SIPAccountMetadata
    {
        [Display(Name = "Password")]
        [Required(ErrorMessage = "The password must be set.")]
        public string SIPPassword { get; set; }
    }
}
