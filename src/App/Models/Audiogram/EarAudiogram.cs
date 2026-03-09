using System;
using System.Collections.Generic;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Models.Audiogram
{
    /// <summary>Audiogram data for one ear (left or right).</summary>
    public sealed class EarAudiogram
    {
        public DeviceSide Side { get; set; }
        public List<AudiogramPoint> Points { get; set; } = new();
        /// <summary>Optional date or session identifier.</summary>
        public DateTime? Date { get; set; }
        public string? Id { get; set; }
    }
}
