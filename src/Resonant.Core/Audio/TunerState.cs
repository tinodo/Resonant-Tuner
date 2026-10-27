namespace Resonant.Core.Audio;

public enum TunerState
{
    Stopped,
    RequestingPermission,
    Starting,
    Listening,
    NoSignal,
    UncertainPitch,
    Clipping,
    DeviceLost,
    Error,
}

/// <summary>Maps capture status plus the latest reading to the explicit tuner state shown to the user.</summary>
public static class TunerStateResolver
{
    public static TunerState Resolve(CaptureStatus capture, PitchReading? reading)
    {
        ArgumentNullException.ThrowIfNull(capture);
        switch (capture.State)
        {
            case CaptureState.Stopped:
                return TunerState.Stopped;
            case CaptureState.RequestingPermission:
                return TunerState.RequestingPermission;
            case CaptureState.Starting:
                return TunerState.Starting;
            case CaptureState.DeviceLost:
                return TunerState.DeviceLost;
            case CaptureState.Error:
                return TunerState.Error;
        }

        if (reading is not { } r)
        {
            return TunerState.Listening;
        }

        if (r.IsClipping)
        {
            return TunerState.Clipping;
        }

        if (r.IsValid)
        {
            return TunerState.Listening;
        }

        return r.Rejection switch
        {
            PitchRejection.Silence => TunerState.NoSignal,
            PitchRejection.InsufficientData => TunerState.Listening,
            _ => TunerState.UncertainPitch,
        };
    }
}
