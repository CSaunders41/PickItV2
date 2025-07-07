using System.Numerics;

namespace PickIt.Services;

public interface IValidationService
{
    bool ValidateSettings(PickItSettings settings);
    void ClampConfigurationValues(PickItSettings settings);
    bool IsConfigurationValid(PickItSettings settings);
} 