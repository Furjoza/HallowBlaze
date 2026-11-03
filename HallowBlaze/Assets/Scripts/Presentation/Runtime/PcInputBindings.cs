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
        /// <summary>Opens or closes the read-only Field Notes.</summary>
        public const KeyCode Notes = KeyCode.N;

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
            KeyCode.Return, KeyCode.KeypadEnter, Space, KeyCode.PageDown, KeyCode.PageUp,
            KeyCode.Home, KeyCode.End, Cancel
        });

        /// <summary>Recognizes confirmation aliases shared by the picker and menu submit path.</summary>
        public static bool IsConfirmation(KeyCode key) => key == Space || key == KeyCode.Return || key == KeyCode.KeypadEnter;
    }
}