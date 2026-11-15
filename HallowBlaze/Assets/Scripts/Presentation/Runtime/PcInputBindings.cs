using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HallowBlaze.Core.Board.Primitives;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>Authoritative PC keys shared by command sampling, modal selection, visual facing and Controls.</summary>
    public static class PcInputBindings
    {
        /// <summary>Opens or cancels a free interaction draft, including nested replacement.</summary>
        public const KeyCode InteractionToggle = KeyCode.F;
        /// <summary>Selects the previous object only inside interaction mode.</summary>
        public const KeyCode PreviousObject = KeyCode.Q;
        /// <summary>Selects the next object only inside interaction mode.</summary>
        public const KeyCode NextObject = KeyCode.E;
        /// <summary>Turns visual facing left only in idle walking mode.</summary>
        public const KeyCode TurnLeft = KeyCode.Q;
        /// <summary>Turns visual facing right only in idle walking mode.</summary>
        public const KeyCode TurnRight = KeyCode.E;
        /// <summary>Waits in walking mode and confirms the selected action in interaction mode.</summary>
        public const KeyCode Space = KeyCode.Space;
        /// <summary>Cancels a draft or leaves a menu when cancellation is legal.</summary>
        public const KeyCode Cancel = KeyCode.Escape;
        /// <summary>Opens or closes the shared read-only atlas and recorded observations.</summary>
        public const KeyCode Notes = KeyCode.N;
        /// <summary>Toggles the informational enemy-intent legend without resolving a turn.</summary>
        public const KeyCode Legend = KeyCode.L;
        /// <summary>Compatible arrow alias for previous interaction object or replacement slot.</summary>
        public const KeyCode PreviousObjectAlias = KeyCode.LeftArrow;
        /// <summary>Compatible arrow alias for next interaction object or replacement slot.</summary>
        public const KeyCode NextObjectAlias = KeyCode.RightArrow;
        /// <summary>Selects the previous explicit action on the selected object.</summary>
        public const KeyCode PreviousAction = KeyCode.UpArrow;
        /// <summary>Selects the next explicit action on the selected object.</summary>
        public const KeyCode NextAction = KeyCode.DownArrow;
        /// <summary>Cycles actions forward, or backward with either Shift key held.</summary>
        public const KeyCode CycleAction = KeyCode.Tab;
        /// <summary>Compatible main Enter confirmation.</summary>
        public const KeyCode Enter = KeyCode.Return;
        /// <summary>Compatible numeric keypad confirmation.</summary>
        public const KeyCode KeypadEnter = KeyCode.KeypadEnter;

        /// <summary>Absolute grid movement, independent of cosmetic facing.</summary>
        public static IReadOnlyDictionary<KeyCode, Direction> Movement { get; } =
            new ReadOnlyDictionary<KeyCode, Direction>(new Dictionary<KeyCode, Direction>
            {
                { KeyCode.W, Direction.North }, { KeyCode.UpArrow, Direction.North },
                { KeyCode.D, Direction.East }, { KeyCode.RightArrow, Direction.East },
                { KeyCode.S, Direction.South }, { KeyCode.DownArrow, Direction.South },
                { KeyCode.A, Direction.West }, { KeyCode.LeftArrow, Direction.West }
            });

        /// <summary>Menu edges supported by the shared focus owner, in deterministic priority order.</summary>
        public static IReadOnlyList<KeyCode> MenuKeys { get; } = Array.AsReadOnly(new[]
        {
            KeyCode.Tab, KeyCode.DownArrow, KeyCode.UpArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.A, KeyCode.D, KeyCode.W, KeyCode.S,
            KeyCode.Return, KeyCode.KeypadEnter, Space, KeyCode.PageDown, KeyCode.PageUp,
            KeyCode.Home, KeyCode.End, Cancel
        });

        /// <summary>Recognizes confirmation aliases shared by the picker and menu submit path.</summary>
        public static bool IsConfirmation(KeyCode key) => key == Space || key == Enter || key == KeypadEnter;

        /// <summary>Builds context-specific help from the actual runtime key definitions, never a separate hard-coded key map.</summary>
        public static IReadOnlyList<ControlHint> GetHints(PcInputContext context)
        {
            var hints = new List<ControlHint>();
            if (context == PcInputContext.Walking)
            {
                foreach (Direction direction in new[] { Direction.North, Direction.West, Direction.South, Direction.East })
                {
                    var keys = new List<KeyCode>();
                    foreach (var binding in Movement) if (binding.Value.Equals(direction)) keys.Add(binding.Key);
                    hints.Add(new ControlHint("Move " + direction.ToString().ToLowerInvariant(), keys.ToArray()));
                }
                hints.Add(new ControlHint("Wait - one turn", Space));
                hints.Add(new ControlHint("Interaction - open / close", InteractionToggle));
                hints.Add(new ControlHint("Visual left - free", TurnLeft));
                hints.Add(new ControlHint("Visual right - free", TurnRight));
                hints.Add(new ControlHint("Atlas", Notes));
                hints.Add(new ControlHint("Enemy intents", Legend));
                hints.Add(new ControlHint("Pause", Cancel));
            }
            else if (context == PcInputContext.Interaction)
            {
                hints.Add(new ControlHint("Previous object / replacement slot", PreviousObject, PreviousObjectAlias, KeyCode.A));
                hints.Add(new ControlHint("Next object / replacement slot", NextObject, NextObjectAlias, KeyCode.D));
                hints.Add(new ControlHint("Previous action: W / " + KeyboardControlsView.KeyName(PreviousAction) + " or Shift + " + CycleAction, PreviousAction, KeyCode.W, CycleAction, KeyCode.LeftShift));
                hints.Add(new ControlHint("Next action", NextAction, KeyCode.S, CycleAction));
                hints.Add(new ControlHint("Confirm selected action", Space, Enter, KeypadEnter));
                hints.Add(new ControlHint("Cancel draft - free", InteractionToggle, Cancel));
            }
            else
            {
                hints.Add(new ControlHint("Next control", KeyCode.Tab, KeyCode.DownArrow, KeyCode.RightArrow, KeyCode.D, KeyCode.S));
                hints.Add(new ControlHint("Previous control: W / A / Up / Left or Shift + Tab", KeyCode.Tab, KeyCode.LeftShift, KeyCode.UpArrow, KeyCode.LeftArrow, KeyCode.A, KeyCode.W));
                hints.Add(new ControlHint("Activate / choose route", Space, KeyCode.Return, KeyCode.KeypadEnter));
                hints.Add(new ControlHint("Back / cancel when available", Cancel));
                hints.Add(new ControlHint("Scroll focused region", KeyCode.PageUp, KeyCode.PageDown, KeyCode.Home, KeyCode.End));
                hints.Add(new ControlHint("Close atlas reader", Notes, Cancel));
                hints.Add(new ControlHint("Adjust focused slider", KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.A, KeyCode.D));
            }
            return hints.AsReadOnly();
        }

        /// <summary>Immutable visible action description associated with actual keys in one input context.</summary>
        public sealed class ControlHint
        {
            internal ControlHint(string label, params KeyCode[] keys) { Label = label; Keys = Array.AsReadOnly(keys); }
            /// <summary>Gets the context-specific player-facing action, including free versus turn-consuming behavior.</summary>
            public string Label { get; }
            /// <summary>Gets the runtime keys highlighted by the diagram.</summary>
            public IReadOnlyList<KeyCode> Keys { get; }
        }
    }

    /// <summary>Distinct player-facing contexts displayed without changing the active gameplay input owner.</summary>
    public enum PcInputContext
    {
        /// <summary>Absolute movement, Wait and free cosmetic facing outside drafts and modals.</summary>
        Walking,
        /// <summary>Free object/action selection and explicit confirmed interaction.</summary>
        Interaction,
        /// <summary>Keyboard focus, route choice, settings and read-only atlas.</summary>
        Menus
    }
}