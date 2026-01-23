# Új Funkciók - Szarvasok Panic Mode Képességei

## 1. 🦌 Dash Animáció és Vertikális Komponens

### Mit csinál?
- Szarvasok dash-olása közben játszik egy speciális **Dash animáció**
- A dash nem csak **előrevisz**, hanem **felfelé is ugrik/vetődik**
- Valódi **jump/leap** effekt fizikai szimulációval

### Paraméterek (PlayerNetworkController)
```csharp
[SerializeField] private float dashForce = 20f;           // Horizontális erő
[SerializeField] private float dashDuration = 0.2f;        // Dash időtartama
[SerializeField] private float dashCooldown = 3f;          // Cooldown
[SerializeField] private float dashJumpForce = 15f;       // ⭐ ÚJ: Vertikális ugrás erő
```

### Hogyan működik?
1. Szarvas lenyomja az **Alt + W/A/S/D** gombot
2. Aktiválódik a **Dash logika**
3. **TriggerDashAnimServerRpc()** szinkronizálja az animációt minden kliens számára
4. **MoveDeer()** során:
   - Horizontálisan mozog: `dashDir * dashForce`
   - Vertikálisan ugrál: `velocity.y = dashJumpForce`
5. A CharacterController mindkét komponenst alkalmazta

### Animator Setup (szükséges)
A Deer Animator-ban add hozzá:
- **Parameter**: `Dash` (Trigger)
- **Transition**: Normal → Jump/Leap animation
- **Zurück**: Jump animation → Normal

---

## 2. 🔥 Hunter Panic Mode - Szarvasok Sebeznek!

### Mit csinál?
- Amikor a **Hunter megsérül és Panic Mode aktiválódik**
- Az összes élő **szarvas képessé válik sebezni a vadászt**
- Szarvasok **+25 sebzést** adnak minden érintésre
- **Panic Mode végéig** marad az effektus

### Új Komponens: HealthComponent
```csharp
public NetworkVariable<bool> isPanicModeActive = new NetworkVariable<bool>(false);
[SerializeField] private float deerPanicDamagePerHit = 25f;
```

### Aktiválás Flow
1. **Hunter megsérül** `OnPlayerDied(isInstaKill: false)`
2. `TriggerHunterPanicMode()` hívódik
3. `TransformToPanicModeClientRpc()` futtatódik **MINDEN szarvasnak**
4. Szarvasok: `healthComponent.SetPanicModeActiveRpc(true)`
5. ✅ **Szarvasok aktív sebezők**

### Collision Detection
**PlayerNetworkController.CheckDeerPanicCollisions()**
```csharp
// LateUpdate-ban futtat (CSAK pánik módban!)
if (!isHunter.Value && isPanicMode && characterController.isGrounded)
{
    CheckDeerPanicCollisions();
}
```

Működése:
- `Physics.OverlapSphere(transform.position, 2f)` - 2m sugarú körön belül
- Keresi a "Player" tag-ú gameobjecteket
- Ha **Hunter** van közelben: `AttackHunterServerRpc()`
- **-25 Health** a vadásznak

---

## 3. ⚔️ Szarvas Támadás Animáció

### Mit csinál?
- Szarvasok **támadó animációt** játszanak panic módban
- A támadás **szinkronizálva van** a hálózaton
- Mindenki látja amikor a szarvas megtámad egy vadászt

### Működés
```csharp
[ServerRpc]
private void AttackHunterServerRpc(ulong hunterNetId)
{
    // CSAK pánik módban lehet támadni!
    if (!NetworkGameManager.Instance.IsHunterPanic()) return;
    
    // Sebzés
    hunterHealth.ModifyHealth(-25f);
    
    // 🎬 ANIMÁCIÓ - SZINKRONIZÁLVA!
    TriggerDeerAttackAnimClientRpc();
}
```

### Animator Setup
Deer Animator-ban:
- **Parameter**: `DeerAttack` (Trigger)
- **Animation**: Attack/Bite/Strike animáció
- **Duration**: ~0.5-1 másodperc

---

## 4. 🎨 Szarvas Transzformáció + BlendTree Váltás

### Mit csinál?
- Szarvasok **vizuálisan megváltoznak** Panic módban
- **BlendTree automatikusan vált** NormalDeer → EvilDeer között
- Az animator parameter szinkronizálódik minden kliens számára

### Új NetworkVariable
```csharp
private NetworkVariable<bool> isDeerEvilMode = 
    new NetworkVariable<bool>(false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server);
```

### Animator Parameter
- **Parameter név**: `IsEvilDeer` (Bool)
- **Default**: false (NormalDeer BlendTree)
- **Panic módban**: true (EvilDeer BlendTree)

### BlendTree Struktúra

#### NormalDeer BlendTree
```
Speed (0-1): Blend parameter
├─ Idle (Speed = 0)
├─ Walk (Speed = 0.5)
└─ Sprint (Speed = 1)
```

#### EvilDeer BlendTree
```
Speed (0-1): Blend parameter
├─ EvilIdle (Speed = 0)
├─ EvilAttackStance (Speed = 0.3)
└─ EvilSprint (Speed = 1)
```

### Animator Beállítás

**Animator States:**
1. Create a **BlendTree** for each
2. Use `IsEvilDeer` bool parameter for switching

```
Deer_Machine
├─ NormalDeer (BlendTree)
│  └─ [transitions based on Speed]
├─ EvilDeer (BlendTree)
│  └─ [transitions based on Speed]
└─ Transitions:
   ├─ NormalDeer → EvilDeer (when IsEvilDeer = true)
   └─ EvilDeer → NormalDeer (when IsEvilDeer = false)
```

### Szinkronizáció

```csharp
[ServerRpc]
private void SetDeerEvilModeServerRpc(bool isEvil)
{
    isDeerEvilMode.Value = isEvil;
}

private void OnDeerEvilModeChanged(bool previous, bool current)
{
    if (animator != null && !isHunter.Value)
    {
        animator.SetBool(animIDIsEvilDeer, current);
    }
}
```

### Pánik Aktiválás
```csharp
if (!isHunter.Value)
{
    // BlendTree váltás: NormalDeer → EvilDeer
    SetDeerEvilModeServerRpc(true);
}
```

### Pánik Deaktiválás (Játék vége)
```csharp
if (IsServer)
{
    isDeerEvilMode.Value = false;  // Vissza NormalDeer
}
```

---

## 📡 Szinkronizáció

### Összes NetworkVariable
```csharp
isDeerEvilMode       // BlendTree mód (Normal ↔ Evil)
isPanicModeActive    // Sebzés engedélyezés
```

### Összes RPC
```csharp
// Server → Client
TriggerDashAnimClientRpc()
TriggerDeerAttackAnimClientRpc()

// Owner → Server → Clients
SetDeerEvilModeServerRpc()
AttackHunterServerRpc()
```

---

## 🎮 Gameplay Loop

### Normál játék
```
Szarvasok: NormalDeer BlendTree
Animáció: Idle, Walk, Sprint
Sebzés: ❌ Letiltva
```

### Hunter megsérül
```
1. OnPlayerDied() → wasHunter=true, isInstaKill=false
2. TriggerHunterPanicMode() aktiválódik
3. Szarvasok: isDeerEvilMode = true
4. BlendTree: NormalDeer → EvilDeer (automatikus)
5. Szarvasok tunak támadni (-25 health)
6. Szarvas animáció: DeerAttack trigger
```

### Túlélés/Vesztés
```
Hunter elég közel ér Safe House-ba → Biztonság
Szarvasok: isDeerEvilMode = false
BlendTree: EvilDeer → NormalDeer
Animáció: Normál idle, walk, sprint
Sebzés: ❌ Letiltva ismét
```

---

## 🔧 Beállítás

### Inspector Paraméterek (PlayerNetworkController)
```csharp
Dash Force = 20         // Horizontális erő
Dash Duration = 0.2     // Dash időtartama
Dash Cooldown = 3       // Cooldown segundumban
Dash Jump Force = 15    // Vertikális erő (UGRÁS!)
```

### Inspector Paraméterek (HealthComponent)
```csharp
Deer Panic Damage Per Hit = 25  // Sebzés érték per hit
```

### Collision sugár
```csharp
Physics.OverlapSphere(transform.position, 2f)  // 2 méter körül
```

---

## ⚠️ Fontos Megjegyzések

1. **Animator paraméterek**: 
   - `Dash` (Trigger)
   - `DeerAttack` (Trigger)
   - `IsEvilDeer` (Bool) ← **LEGFONTOSABB a BlendTree-hez!**

2. **BlendTree váltás**: Az `IsEvilDeer` bool paraméter határozza meg a BlendTree-t

3. **Tag-ek**: Szarvasoknak és Hunternek "Player" tag-nek kell lenni

4. **Collider**: Szarvasok ColliderComponent kell, hogy legyen

5. **Physics Update**: `isPanicMode` ellenőrzés csak `characterController.isGrounded`-nél

6. **Sebzés ellenőrzés**: 
   - `CheckDeerPanicCollisions()` - CSAK pánik módban
   - `AttackHunterServerRpc()` - DUPLA ellenőrzés

---

## 🐛 Debug

Konzol üzenetek:
```
[PlayerNetworkController] Szarvas megtámadott egy vadászt!
[PlayerNetworkController] Szarvas mód: Evil/Panic
[PlayerNetworkController] Szarvas mód: Normal
[Player] Szarvas átváltozik Panic Module-ban - Sebzés képessége aktiválva!
```

---

## ✅ Teszt Checklist

- [ ] Szarvas tudjon dash-olni (Alt + W/A/S/D)
- [ ] Dash animáció lejátszódik
- [ ] Szarvas felfelé ugrik dash közben
- [ ] Hunter megsérül → Panic Mode aktiválódik
- [ ] BlendTree váltás: NormalDeer → EvilDeer (animator)
- [ ] Szarvasok tunak támadni (OverlapSphere működik)
- [ ] Vadász `-25 health` -t kap szarvas érintésre
- [ ] Szarvas támadás animáció lejátszódik
- [ ] **Összes kliens látja** a BlendTree váltást és animációkat
- [ ] Szinkronizáció helyes (minden kliens látja)
- [ ] Pánik vége: BlendTree vissza NormalDeer-re

