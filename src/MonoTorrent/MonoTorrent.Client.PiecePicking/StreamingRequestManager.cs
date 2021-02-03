﻿using System;
using System.Collections.Generic;
using System.Text;

using MonoTorrent.Client.Messages;
using MonoTorrent.Client.Messages.Standard;

namespace MonoTorrent.Client.PiecePicking
{
    class StreamingRequestManager : IPieceRequester
    {
        ITorrentData TorrentData { get; set; }

        public bool InEndgameMode { get; private set; }
        public StreamingPiecePicker Picker { get; private set; }

        IPiecePicker IPieceRequester.Picker => Picker;

        public void Initialise (ITorrentData torrentData, IReadOnlyList<BitField> ignoringBitfields)
        {
            TorrentData = torrentData;

            // IPiecePicker picker = new StandardPicker ();
            // picker = new RandomisedPicker (picker);
            // picker = new RarestFirstPicker (picker);
            // picker = new PriorityPicker (picker);

            // Picker = IgnoringPicker.Wrap (picker, ignoringBitfields);

            Picker = new StreamingPiecePicker ();
            Picker.Initialise (torrentData);
        }

        public void AddRequests (IReadOnlyList<IPeerWithMessaging> peers, BitField bitfield)
        {
            foreach (var peer in peers)
                AddRequests (peer, peers, bitfield);
        }

        public void AddRequests (IPeerWithMessaging peer, IReadOnlyList<IPeerWithMessaging> allPeers, BitField bitfield)
        {
            int maxRequests = peer.MaxPendingRequests;

            if (!peer.CanRequestMorePieces)
                return;

            int count = peer.PreferredRequestAmount (TorrentData.PieceLength);

            if (!peer.IsChoking || peer.SupportsFastPeer) {
                while (peer.AmRequestingPiecesCount < maxRequests) {
                    PieceRequest? request = Picker.ContinueExistingRequest (peer, 0, peer.BitField.Length - 1);
                    if (request != null)
                        peer.EnqueueRequest (request.Value);
                    else
                        break;
                }
            }

            if (!peer.IsChoking || (peer.SupportsFastPeer && peer.IsAllowedFastPieces.Count > 0)) {
                while (peer.AmRequestingPiecesCount < maxRequests) {
                    IList<PieceRequest> request = Picker.PickPiece (peer, peer.BitField, allPeers, count, 0, TorrentData.PieceCount () - 1);
                    if (request != null && request.Count > 0)
                        peer.EnqueueRequests (request);
                    else
                        break;
                }
            }

            if (!peer.IsChoking && peer.AmRequestingPiecesCount == 0) {
                while (peer.AmRequestingPiecesCount < maxRequests) {
                    PieceRequest? request = Picker.ContinueAnyExistingRequest (peer, 0, TorrentData.PieceCount () - 1, 1);
                    // If this peer is a seeder and we are unable to request any new blocks, then we should enter
                    // endgame mode. Every block has been requested at least once at this point.
                    if (request == null && (InEndgameMode || peer.IsSeeder)) {
                        request = Picker.ContinueAnyExistingRequest (peer, 0, TorrentData.PieceCount () - 1, 2);
                        // FIXME: What if the picker is choosing to not allocate pieces? Then it's not endgame mode.
                        // This should be deterministic, not a heuristic?
                        InEndgameMode |= request != null && (bitfield.Length - bitfield.TrueCount) < 10;
                    }

                    if (request != null)
                        peer.EnqueueRequest (request.Value);
                    else
                        break;
                }
            }
        }
    }
}
