// The runtime split, in one place.
//
// Il2CppInterop generates the game's managed surface with an "Il2Cpp" prefix on every root
// namespace; the Mono assembly has the same namespaces without it. Measured at game
// 0.4.7f12: 2,076 ScheduleOne types on each side, zero divergence in type names. So the
// prefix is a safe mechanical alias.
//
// What is NOT safe is assuming members match. Also measured: LoadManager.onPreLoad is a
// FIELD on Mono and a PROPERTY on IL2CPP, and the same holds for onLoadComplete,
// onSaveStart and onSaveComplete. Names carried over perfectly; shapes did not. Where a
// shape differs, resolve it reflectively once at startup rather than naming it directly —
// a lifecycle hook that fails silently is the worst thing this layer can ship.
//
// These aliases cannot be `global using` because that is a C# 10 feature and this project
// targets C# 9 (see Directory.Build.props — C# 10 string interpolation breaks on Mono).
// So each file that touches the game opens with this block, verbatim, and contains no
// other #if. If a #if appears in a method body, the difference belongs here instead.
//
//   #if MDS_IL2CPP
//   using GameDevUtils    = Il2CppScheduleOne.DevUtilities;
//   using GamePersistence = Il2CppScheduleOne.Persistence;
//   using GameMoney       = Il2CppScheduleOne.Money;
//   using GameTimeNs      = Il2CppScheduleOne.GameTime;
//   using GameEconomy     = Il2CppScheduleOne.Economy;
//   using GameItems       = Il2CppScheduleOne.ItemFramework;
//   using GameProduct     = Il2CppScheduleOne.Product;
//   using GamePlayer      = Il2CppScheduleOne.PlayerScripts;
//   using GameNpcs        = Il2CppScheduleOne.NPCs;
//   #elif MDS_MONO
//   using GameDevUtils    = ScheduleOne.DevUtilities;
//   using GamePersistence = ScheduleOne.Persistence;
//   using GameMoney       = ScheduleOne.Money;
//   using GameTimeNs      = ScheduleOne.GameTime;
//   using GameEconomy     = ScheduleOne.Economy;
//   using GameItems       = ScheduleOne.ItemFramework;
//   using GameProduct     = ScheduleOne.Product;
//   using GamePlayer      = ScheduleOne.PlayerScripts;
//   using GameNpcs        = ScheduleOne.NPCs;
//   #else
//   #error Define MDS_MONO or MDS_IL2CPP.
//   #endif
//
// Nothing in v0.1 names a game type directly — the lifecycle and save layers reach the
// game reflectively — so no file carries this block yet. Stage 2's wrappers will.
