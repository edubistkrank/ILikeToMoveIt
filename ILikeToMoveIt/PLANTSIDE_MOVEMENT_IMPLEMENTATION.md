# Plantside Movement Implementation

## Overview
Added support for moving plantside pieces (base face pieces like RoomPlanterSide, CorridorPlanterSide, etc.).

## Key Changes

### 1. Detection (`TryMoveTargetedLocker`)
- Detects `BaseDeconstructable` components when Alt is held
- Uses reflection to read private `recipe` field to determine TechType
- Falls back to regular `Constructable` detection

### 2. Deconstruction (`TryMoveBaseFacePiece`)
- Calls vanilla `BaseDeconstructable.Deconstruct()` method
- This creates an unconstructed `ConstructableBase` ghost object
- The original BaseDeconstructable is destroyed by vanilla's deconstruct process

### 3. Placement Interception (`Builder_CreateGhost_Patch`)
- **Critical patch**: Intercepts `Builder.CreateGhost()` during face piece moves
- Instead of creating a new ghost, uses the existing `ConstructableBase` from `Deconstruct()`
- This prevents duplicate blueprints from being created

### 4. Session Flow
```
Alt+Click on built plantside
  ↓
TryMoveBaseFacePiece() sets moveSessionIsFacePiece = true
  ↓
Calls vanilla Deconstruct() which:
  - Creates ConstructableBase (unconstructed)
  - Destroys the original BaseDeconstructable
  - Clears the base face
  ↓
BeginPlacingAsync() starts Builder
  ↓
Builder.CreateGhost() - OUR PATCH:
  - Finds the existing ConstructableBase
  - Uses it instead of creating new
  ↓
Builder placement mode active
  ↓
Place new location → TryPlace() succeeds
  ↓
Builder_TryPlace_Patch marks committed = true and ends session
```

## Behavior Notes

### What Works
- Alt+Click on a built plantside correctly enters placement mode
- The ghost shows the correct placement preview
- Placing at new location works as expected
- Vanilla collision detection and face orientation checks still apply

### Cancellation
- If you cancel (press Esc) before placing:
  - The ConstructableBase remains unconstructed at original location
  - This mimics vanilla behavior - deconstruction is permanent
  - You can complete construction or leave it as-is

### Supported Plantside Pieces
- RoomPlanterSide
- CorridorIShapePlanterSide (and other corridor variants)
- MoonpoolPlanterSide
- MapRoomPlanterSide
- LargeRoomPlanterSide
- ControlRoomPlanterSide

## Technical Details

### Why Special Handling?
Plantside pieces are not simple `Constructable` objects. They are:
- `BaseDeconstructable` components
- Tied to base structure geometry
- Represented as `Base.Face` with `Base.FaceType.Planter`
- Require special ghost (BaseGhost) and placement logic

### Vanilla Flow Reproduction
The implementation follows vanilla's deconstruction/placement flow:
1. `BaseDeconstructable.Deconstruct()` is the entry point
2. Vanilla creates `ConstructableBase` prefab instance
3. Vanilla's `BaseGhost.Deconstruct()` configures the ghost
4. The resulting object enters builder placement mode

By intercepting `Builder.CreateGhost()`, we prevent duplication and ensure the existing ghost is used.

### Session State
- `moveSessionIsFacePiece`: Flag indicating this is a face piece move
- `moveSessionFacePieceSource`: Reference to original BaseDeconstructable
- Other state fields are shared with regular item moves

## Configuration
Plantside moves respect the same mod settings as other constructables:
- Check `ModConfig.cs` for category toggles
- Face pieces are validated via `IsMovableBySettings(TechType)`

## Future Improvements
- Add dedicated toggle for plantside movement in ModConfig
- Add visual feedback for face piece orientation
- Consider restoration mechanics if needed
