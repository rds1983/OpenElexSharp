# Audio (`snd` / `dlg`)

All ELEX audio resources are FMOD **FSB5** sound banks inside the standard GAR5 resource wrapper.
`snd` stores sound effects, music and ambience; `dlg` stores dialogue voice lines. There are no
standalone `.fsb` or `.fev` files in the compiled asset catalogue.

## Purpose and where they appear

| Type | Class | Count | Contents |
| --- | --- | ---: | --- |
| `snd` | `SN11` resource database | 3,524 | Music, ambience, effects and radio tracks. |
| `dlg` | `DI22` resource database | 31,494 | Localized dialogue voice lines. |

Logical names resolve through `w_snd_*_*.db` and `w_dlg_*_*.db`, for example
`0_en_snd/INFO_IKENI7_00001457_English.elexsnd` and
`0_en_dlg/INFO_DIRKSI5_00000006_English.elexdlg`. The same name can exist in both databases; the
payloads are independent sound banks.

## Container

```text
GAR5                     outer resource wrapper (44 bytes observed)
  uint32 0x20
  byte[36] zero/resource metadata
FSB5                     FMOD sound bank
  ...
```

The FSB5 payload starts at offset `0x2C`. A 36-byte trailer may follow the sample data; it is not part
of the FSB5 `sampleDataSize`.

## FSB5 header

| Offset | Type | Meaning |
| ---: | --- | --- |
| 0x00 | `char[4]` | `FSB5`. |
| 0x04 | `uint32` | Version, `0` or `1`; ELEX uses `1`. |
| 0x08 | `uint32` | Sample count; ELEX banks contain exactly one. |
| 0x0C | `uint32` | Sample-header bytes (`36` in ELEX). |
| 0x10 | `uint32` | Name-table bytes; `0` in ELEX. |
| 0x14 | `uint32` | Encoded sample-data bytes. |
| 0x18 | `uint32` | FMOD codec id; ELEX uses `0x10` (`FADPCM`). |
| 0x1C | `uint32` | Zero. |
| 0x20 | `uint32` | Version-1 flags. |
| 0x24 | `byte[16]` | Name hash. |
| 0x34 | `byte[8]` | Sub-hash. |
| 0x3C | `Sample[]` | Sample headers (version 1; version 0 uses a `0x40` base header). |

### Sample header

Each sample starts with a `uint64` mode word:

| Bits | Meaning |
| --- | --- |
| 63–34 | PCM frame count (30 bits). |
| 33–8 | Data offset in 32-byte units. |
| 7–6 | Channel code: `0` = 1, `1` = 2, `2` = 6, `3` = 8 channels. |
| 5–1 | Sample-rate code: `0`=4 kHz, `1`=8 kHz, `2`=11 kHz, `3`=11.025 kHz, `4`=16 kHz, `5`=22.05 kHz, `6`=24 kHz, `7`=32 kHz, `8`=44.1 kHz, `9`=48 kHz, `10`=96 kHz. |
| 0 | Extra-flag chain follows the mode word. |

Extra flags are `uint32` words: bits 32–26 are the type, 25–1 the payload size, and bit 0 means another
flag follows. Recognized types include `1` (channel override), `2` (sample-rate override), `3` (loop
start/end), `7`/`9` (codec setup), `11` (Vorbis seek table) and `14` (Vorbis layers). ELEX banks use none
of them; their 36-byte sample headers are a bare mode word plus padding.

Sample data starts at `0x2C + baseHeaderSize + sampleHeaderSize + nameTableSize`, plus the sample's
32-byte-unit data offset. A bank with one sample uses `sampleDataSize` directly.

## Codec

ELEX uses **FADPCM** (FMOD codec `0x10`) for every `snd` and `dlg` resource. FADPCM stores blocks of
predictor-coded 4-bit deltas; playback requires an FADPCM decoder (FMOD or a compatible implementation).
T012 only exposes metadata. Decoding and playback are deferred to M3.

The known FMOD codec ids are listed in `FsbSoundBank.GetCodecName` (PCM8/16/24/32, float, GCADPCM,
IMA ADPCM, VAG, HEVAG, XMA, MPEG, CELT, ATRAC9, XWMA, Vorbis, FADPCM, Opus).

## Retail validation

All **35,018** audio banks parse with this layout.

| Metric | Value |
| --- | ---: |
| `snd` / `dlg` resources | 3,524 / 31,494 |
| FSB5 version | 1 (all) |
| Codec | FADPCM (all) |
| Mono / stereo banks | 34,563 / 455 |
| Sample rates | 48 kHz (31,760), 44.1 kHz (3,257), 22.05 kHz (1) |
| Total declared duration | 136,827.9 s (38.01 h) |
| Total encoded audio bytes | 4,325,548,672 |
| Longest bank | `sfx_int_radio_mus_05`, 433.656 s |
| Banks with multiple samples | 0 |
| Banks with a name table | 0 |

Example:

```text
INFO_IKENI7_00001457_English  dlg  1  FADPCM  1  1  48000  0.971  25504
mus_beach_d_e_01             snd  1  FADPCM  1  2  48000  208.805  10962304
INFO_DIRKSI5_00000006_English dlg  1  FADPCM  1  1  48000  2.245  58944
```

The `.bik` files under `data/extern/videos` are Bink video containers with their own audio streams;
they are not part of the `snd`/`dlg` audio catalogue and will be handled with video playback.

## Open questions

- Why some names exist in both the `snd` and `dlg` databases with separate payloads.
- The 36-byte trailer after the FSB5 sample data.
- FADPCM block/predictor layout and whether the same decoder is needed for music and voice.
- Whether any mod or non-English volume uses a different FSB5 version, codec or multi-sample bank.

## Implementation status

| Component | File |
| --- | --- |
| FSB5 metadata parser | `OpenElexSharp/Assets/FsbSoundBank.cs` |
| CLI inspector | `OpenElexSharp/Assets/AssetCatalogConsole.cs` (`assets sounds`) |
| Tests | `OpenElexSharp.Tests/FsbSoundBankTests.cs` |

Commands:

```powershell
dotnet run --project OpenElexSharp -- assets sounds IKENI7 --limit 5 --properties
dotnet run --project OpenElexSharp -- assets sounds mus_beach --limit 5
dotnet run --project OpenElexSharp -- assets sounds --all
```

Output columns: name, type token, FSB5 version, codec, sample count, first-sample channels, sample rate,
total duration, sample-data bytes and ROM path. `--properties` adds one row per sample with its name,
channels, rate, frame count, duration, encoded bytes and loop range.

## References

- FSB5 layout cross-checked with the open-source vgmstream project (ISC license).
- FMOD codec identifiers follow the public FMOD sound-format enumeration.

## Related tasks

- T012 — this specification and parser.
- T405/T407 — audio playback and parity.
