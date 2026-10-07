using System;
using System.Collections.Generic;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Turns.Contracts;
using UnityEngine;

namespace HallowBlaze.Presentation.Runtime
{
    /// <summary>
    /// Samples PC key-down edges at most once per frame, producing one complete cardinal move or explicit Wait.
    /// Ambiguous chords are free no-command samples. No input is buffered and no target interaction is bound.
    /// </summary>
    public sealed class PcCommandSource : ICommandSource
    {
        private readonly Dictionary<KeyCode, Func<PlayerCommand>> bindings = new Dictionary<KeyCode, Func<PlayerCommand>>();
        private readonly Func<KeyCode, bool> keyDown;
        private readonly Func<int> frameNumber;
        private int? sampledFrame;

        /// <summary>Creates arrows/WASD movement and Space Wait bindings without installing production polling.</summary>
        /// <param name="keyDown">Key-down sampler, or Unity's current-frame sampler when null.</param>
        /// <param name="frameNumber">Frame identity, or Unity's frame count when null; injectable for deterministic tests.</param>
        public PcCommandSource(Func<KeyCode, bool> keyDown = null, Func<int> frameNumber = null)
        {
            this.keyDown = keyDown ?? Input.GetKeyDown;
            this.frameNumber = frameNumber ?? (() => Time.frameCount);
            Bind(KeyCode.UpArrow, () => new MoveCommand(Direction.North));
            Bind(KeyCode.W, () => new MoveCommand(Direction.North));
            Bind(KeyCode.RightArrow, () => new MoveCommand(Direction.East));
            Bind(KeyCode.D, () => new MoveCommand(Direction.East));
            Bind(KeyCode.DownArrow, () => new MoveCommand(Direction.South));
            Bind(KeyCode.S, () => new MoveCommand(Direction.South));
            Bind(KeyCode.LeftArrow, () => new MoveCommand(Direction.West));
            Bind(KeyCode.A, () => new MoveCommand(Direction.West));
            Bind(KeyCode.Space, () => new WaitCommand());
        }

        /// <summary>Adds or replaces a key's complete-command factory without enumerating future command types.</summary>
        /// <param name="key">PC key producing the intent.</param>
        /// <param name="commandFactory">Builds the whole command; it must not mutate or resolve gameplay.</param>
        public void Bind(KeyCode key, Func<PlayerCommand> commandFactory)
        {
            bindings[key] = commandFactory ?? throw new ArgumentNullException(nameof(commandFactory));
        }

        /// <summary>Consumes a frame sample once; zero or multiple bound key edges produce no intent.</summary>
        /// <param name="command">The complete intent, or null when this sample is absent, ambiguous, or consumed.</param>
        /// <returns>Whether one non-null complete command was built.</returns>
        public bool TryTakeCommand(out PlayerCommand command)
        {
            command = null;
            int frame = frameNumber();
            if (sampledFrame == frame)
                return false;
            sampledFrame = frame;
            Func<PlayerCommand> selected = null;
            foreach (var binding in bindings)
            {
                if (!keyDown(binding.Key))
                    continue;
                if (selected != null)
                    return false;
                selected = binding.Value;
            }
            command = selected?.Invoke();
            return command != null;
        }

        /// <summary>Discards the current frame's input for free; this reference source has no buffered modal draft.</summary>
        public void CancelDraft() => sampledFrame = frameNumber();
    }
}