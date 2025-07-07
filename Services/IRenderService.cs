using System.Collections.Generic;
using SharpDX;

namespace PickIt.Services;

public interface IRenderService
{
    void RenderInventoryOverlay();
    void RenderDebugHighlights();
    void RenderItemHighlight(PickItItemData item, Color color);
    void RenderAll();
}

public interface IValidationService
{
    bool ValidateSettings(PickItSettings settings);
    void ClampConfigurationValues(PickItSettings settings);
    bool IsConfigurationValid(PickItSettings settings);
} 