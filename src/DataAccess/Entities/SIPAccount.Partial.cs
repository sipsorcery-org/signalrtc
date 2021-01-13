// ============================================================================
// FileName: SIPAccount.Partial.cs
//
// Description:
// Represents the SIPAccount entity. This partial class is used to apply 
// additional properties or metadata to the audo generated SIPAccount class.
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

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc;
using SIPSorcery.SIP.App;

#nullable disable

namespace devcall.DataAccess
{
    [ModelMetadataType(typeof(SIPAccountMetadata))]
    public partial class SIPAccount : ISIPAccount
    {
        public string SIPDomain => Domain?.Domain;

        /// <summary>
        /// Address of Record. Convenience property.
        /// </summary>
        [Display(Name = "SIP Account Address")]
        public string AOR => SIPUsername + "@" + Domain?.Domain;

        [NotMapped]
        public string SIPPassword { get; set; }
    }

    public class SIPAccountMetadata
    {
        [Display(Name = "Password")]
        [Required(ErrorMessage = "The password must be set.")]
        public string SIPPassword { get; set; }
    }
}
