namespace XTSPrimeMoverProject.Models
{
    /// <summary>
    /// A single in-process measurement taken when a station finishes its operation
    /// (e.g. weld penetration depth, screw torque, insulation resistance).
    /// Quality is decided by the measured value against its specification limits,
    /// not by a coin flip.
    /// </summary>
    public sealed class StationMeasurement
    {
        public string Characteristic { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public double Value { get; init; }
        public double Nominal { get; init; }
        public double? LowerSpecLimit { get; init; }
        public double? UpperSpecLimit { get; init; }
        public bool InSpec { get; init; }
        public string StationName { get; init; } = string.Empty;
        public int MachineId { get; init; }

        public string Format()
        {
            string lsl = LowerSpecLimit.HasValue ? LowerSpecLimit.Value.ToString("0.###") : "-";
            string usl = UpperSpecLimit.HasValue ? UpperSpecLimit.Value.ToString("0.###") : "-";
            return $"{Characteristic}={Value:0.###}{Unit} [{lsl}..{usl}] {(InSpec ? "OK" : "NOK")}";
        }
    }

    /// <summary>
    /// Physics/health-aware process model a station consults for its cycle time
    /// and its measurement. Implemented by the digital twin in the intelligence layer.
    /// </summary>
    public interface IStationProcessModel
    {
        /// <summary>Multiplier (>= 1) applied to the nominal process time, e.g. a worn spindle runs slower.</summary>
        double GetCycleTimeFactor(Station station);

        /// <summary>Produces the measurement for the part that just finished this station.</summary>
        StationMeasurement? Measure(Station station, Part part);
    }

    /// <summary>
    /// Physical XTS track geometry. Mover positions are kept in degrees around the
    /// loop for compatibility with the PLC logic; this converts them to SI units.
    /// </summary>
    public static class XtsTrackGeometry
    {
        /// <summary>24 motor modules x 250 mm.</summary>
        public const double TrackLengthMeters = 6.0;
        public const double MetersPerDegree = TrackLengthMeters / 360.0;

        public static double ToMeters(double degrees) => degrees * MetersPerDegree;
    }
}
