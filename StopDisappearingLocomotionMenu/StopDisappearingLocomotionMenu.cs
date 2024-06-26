﻿using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace StopDisappearingLocomotionMenu
{
    public class StopDisappearingLocomotionMenu : ResoniteMod
    {
        public static ModConfiguration Config;

        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> ShowLocomotion = new ModConfigurationKey<bool>("ShowLocomotion", "Makes the Locomotion picker appear on the context menu even with a tool equipped.", () => true);

        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> ShowScale = new ModConfigurationKey<bool>("ShowScale", "Shows the Scale toggle as well when showing the Locomotion picker.", () => false);

        public override string Author => "Banane9";
        public override string Link => "https://github.com/Banane9/ResoniteStopDisappearingLocomotionMenu";
        public override string Name => "StopDisappearingLocomotionMenu";
        public override string Version => "1.0.0";

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony($"{Author}.{Name}");
            Config = GetConfiguration();
            Config.Save(true);

            var commonToolType = typeof(InteractionHandler);
            foreach (Type t in commonToolType.GetNestedTypes(AccessTools.allDeclared))
            {
                try
                {
                    var asyncType = t.GetNestedTypes(AccessTools.allDeclared).First(type => type.Name == "<<OpenContextMenu>b__0>d");
                    var methodToPatch = asyncType.GetMethod("MoveNext", AccessTools.allDeclared);

                    var transpilerMethod = typeof(StopDisappearingLocomotionMenu).GetMethod(nameof(ContextMenuGeneratorTranspiler), AccessTools.allDeclared);

                    harmony.Patch(methodToPatch, transpiler: new HarmonyMethod(transpilerMethod));
                    break;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
            }
        }

        private static bool CheckHideLocomotionAndScale()
        {
            // inverted because the IL jumps over the generation code on true
            return !Config.GetValue(ShowLocomotion);
        }

        private static bool CheckShowScale()
        {
            return !Config.GetValue(ShowLocomotion) || Config.GetValue(ShowScale);
        }

        private static IEnumerable<CodeInstruction> ContextMenuGeneratorTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
        {
            var list = new List<CodeInstruction>(instructions);

            var screenActiveGetter = typeof(InputInterface).GetProperty(nameof(InputInterface.ScreenActive)).GetGetMethod();
            var canScaleGetter = typeof(InteractionHandler).GetProperty(nameof(InteractionHandler.CanScale), AccessTools.allDeclared).GetGetMethod();

            var screenActiveIndex = list.FindIndex(instruction => instruction.Calls(screenActiveGetter));
            var hideLocomotionAndScaleCheckMethod = typeof(StopDisappearingLocomotionMenu).GetMethod(nameof(CheckHideLocomotionAndScale), AccessTools.allDeclared);

            CodeInstruction holdingToolVar = null;

            // Goal: if (!holdingTool || !CheckHideLocomotionAndScale() || ScreenActive) [insert Locomotion and (maybe) Scale buttons]
            for (var i = screenActiveIndex - 1; i >= 0; --i)
            {
                if (list[i].opcode == OpCodes.Ldloc_S && list[i + 1].opcode == OpCodes.Brfalse_S)
                {
                    holdingToolVar = list[i];

                    list.InsertRange(i + 2, new[]
                    {
                        new CodeInstruction(OpCodes.Call, hideLocomotionAndScaleCheckMethod),
                        list[i + 1] // re-use jump-on-false into body from !holdingTool
                    });

                    break;
                }
            }

            var canScaleIndex = list.FindIndex(screenActiveIndex, instruction => instruction.Calls(canScaleGetter));
            var showScaleCheckMethod = typeof(StopDisappearingLocomotionMenu).GetMethod(nameof(CheckShowScale), AccessTools.allDeclared);

            var scaleBodyLabel = ilGenerator.DefineLabel();
            list[canScaleIndex + 2].labels.Add(scaleBodyLabel);

            // Insert after the jump for CanScale
            // Goal: if (CanScale && (!holdingTool || CheckShowScale())) [insert Scale button]
            list.InsertRange(canScaleIndex + 2, new[]
            {
                holdingToolVar,
                new CodeInstruction(OpCodes.Brfalse_S, scaleBodyLabel),
                new CodeInstruction(OpCodes.Call, showScaleCheckMethod),
                list[canScaleIndex + 1] // re-use jump-on-false over body from CanScale to build an &&
            });

            return list;
        }
    }
}