namespace Ul8ziz.FittingApp.App.Models.Audiogram
{
    /// <summary>Audiogram measurement type: Air Conduction, Bone Conduction, or Uncomfortable Loudness Level.</summary>
    public enum AudiogramPointType
    {
        AC = 0,
        BC = 1,
        UCL = 2
    }

    /// <summary>Single audiogram point: frequency (Hz), hearing threshold (dB HL), optional UCL (dB HL), and measurement type.</summary>
    public sealed class AudiogramPoint
    {
        public double FrequencyHz { get; set; }

        /// <summary>Hearing threshold or UCL value in dB HL. Null if not measured.</summary>
        public int? ThresholdDbHL { get; set; }

        /// <summary>Uncomfortable loudness level in dB HL. Legacy field — use ThresholdDbHL with PointType=UCL for new data.</summary>
        public int? UclDbHL { get; set; }

        /// <summary>Measurement type (AC, BC, UCL). Defaults to AC for backward compatibility with old JSON files.</summary>
        public AudiogramPointType PointType { get; set; } = AudiogramPointType.AC;

        public AudiogramPoint() { }

        public AudiogramPoint(double frequencyHz, int? thresholdDbHL, int? uclDbHL = null,
            AudiogramPointType pointType = AudiogramPointType.AC)
        {
            FrequencyHz = frequencyHz;
            ThresholdDbHL = thresholdDbHL;
            UclDbHL = uclDbHL;
            PointType = pointType;
        }
    }
}
