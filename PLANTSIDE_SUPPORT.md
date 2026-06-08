# Soporte para Plantside - Documento de Cambios

## Objetivo
Agregar soporte para mover elementos `plantside` (macetas de pared: RoomPlanterSide, CorridorIShapePlanterSide, etc.) al mod "I Like To Move It".

## Problema Identificado
Los `plantside` son piezas de cara (`BaseDeconstructable` con `FaceType.Planter`) que NO son `Constructable` simples. El mod original solo detectaba `Constructable` estándar, por lo que los plantside no se reconocían al apuntarlos.

## Cambios Realizados en Plugin.cs

### 1. **Nueva Variable de Estado** (Línea 25)
```csharp
private static bool moveSessionIsFacePiece;
```
- Distingue entre sesiones de movimiento de items regulares vs. face pieces.

### 2. **Refactorización de IsMovableBySettings** (Líneas 69-77, 188-211)
- Se dividió en dos métodos:
  - `IsMovableBySettings(Constructable)`: Para items simples
  - `IsMovableBySettings(TechType)`: Para cualquier TechType (plantside incluido)
- Los plantside usan grupos `TechGroup.InteriorModules`, por lo que respetan la configuración existente.

### 3. **Nuevo Método: GetBaseDeconstructableTechType** (Líneas 162-173)
```csharp
private static TechType GetBaseDeconstructableTechType(BaseDeconstructable baseDecon)
```
- Usa Reflection para obtener el tipo de receta desde `BaseDeconstructable.recipe` (campo privado).
- Es necesario porque `BaseDeconstructable` no expone públicamente el `TechType`.

### 4. **Nuevo Método: TryMoveBaseFacePiece** (Líneas 154-180)
```csharp
private static bool TryMoveBaseFacePiece(BaseDeconstructable baseDecon)
```
- Maneja la lógica específica para mover face pieces.
- Llama a `baseDecon.Deconstruct()`, que es el método vanilla del juego que convierte el face piece en un `ConstructableBase` con ghost model.

### 5. **Refactorización de TryMoveTargetedLocker** (Líneas 125-153)
- Ahora intenta detectar `Constructable` primero.
- Si falla, busca `BaseDeconstructable` a distancia ≤ 11f.
- Mantiene la distancia máxima de 30f para `Constructable` regular.

### 6. **Refactorización de Builder_TryPlace_Patch** (Líneas 277-321)
- Divide la lógica en:
  - `HandleFacePiecePlacement()`: Para plantside (sin necesidad de hacer nada extra, el ghost ya está listo).
  - `HandleRegularItemPlacement()`: Para items simples (mantiene lógica original).

### 7. **Refactorización de Builder_End_Patch** (Líneas 323-372)
- Diferencia el rollback según el tipo de sesión.
- Para face pieces: Limpia el `ConstructableBase` creado si se cancela.
- Para items regulares: Restaura posición/rotación original.

## Flujo de Movimiento para Plantside

1. **Detección (Alt + Izquierdo)**
   - Usuario apunta al plantside y presiona Alt + Click izquierdo
   - Se busca `BaseDeconstructable` en el target
   - Se valida distancia (≤ 11f)

2. **Inicio de Sesión**
   - Se llama `baseDecon.Deconstruct()` (método vanilla)
   - Esto automáticamente:
	 - Crea un `ConstructableBase` con su ghost model
	 - El ghost está en modo "unconstructed"
	 - El Builder puede actualizar su posición en tiempo real

3. **Colocación (Click izquierdo nuevamente)**
   - El Builder.TryPlace() sucede
   - El ghost model ya contiene la posición correcta
   - El ConstructableBase se mantiene tal cual el Builder lo posicionó

4. **Cancelación (Presionar Back / ESC)**
   - Builder.End() se ejecuta
   - Se destruye el ConstructableBase creado
   - **NOTA**: El plantside original Ya Fue Desconstructado, así que no se puede restaurar automáticamente
   - Esto es un efecto secundario de usar `Deconstruct()` que es el método vanilla

## Tipos de Plantside Soportados
- `RoomPlanterSide` (TechType.BasePlanter)
- `CorridorIShapePlanterSide`
- `MoonpoolPlanterSide` / `MoonpoolPlanterSideShort`
- `MapRoomPlanterSide`
- `LargeRoomPlanterSide` / `LargeRoomPlanterSideShort`
- `ControlRoomPlanterSide`

Todos ellos son controlados por la configuración `AllowInteriorModules` del mod.

## Limitaciones Conocidas

1. **No se puede revertir una sesión de plantside cancelada**: Una vez que el usuario apunta y presiona Alt+Click, el plantside es desconstructado. Si cancela la sesión, el plantside desaparece. Esto es inherente al uso de `Deconstruct()`.

2. **Solo funciona a distancia ≤ 11f**: Los face pieces tienen un límite de distancia de 11 metros (restricción vanilla).

3. **Reflejo para TechType**: Se usa Reflection para acceder al campo privado `recipe`. Aunque es una práctica segura, podría romperse si Subnautica actualiza esta estructura.

## Testing Recomendado

1. Construir un plantside en una pared/corredor
2. Apuntar con Alt presionado
3. Presionar Click izquierdo
4. Mover a una nueva posición válida
5. Presionar Click izquierdo nuevamente para confirmar
6. Verificar que el plantside se reubicó correctamente

## Compilación
El código compila correctamente sin errores. Se requiere que Subnautica esté instalado para referenciar los ensamblados necesarios.
