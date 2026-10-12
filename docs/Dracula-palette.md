# Verified Dracula Classic palette

Verified 2026-10-12 against the [official Dracula specification](https://draculatheme.com/spec) and [official theme repository](https://github.com/dracula/dracula-theme). This is the free Classic dark theme. Applied to NullTV after your approval: Dracula Classic and Dracula OLED are available; cyan chrome is omitted according to your earlier preference.

The four colors you described are purple **#BD93F9**, deeper UI purple **#815CD6**, yellow **#F1FA8C** and green **#50FA7B**. “Dark Purple” is not a core token; #815CD6 is officially named Functional Purple. [Specification](https://draculatheme.com/spec).

| Classic token | Hex |
|---|---|
| Background | #282A36 |
| Foreground | #F8F8F2 |
| Selection | #44475A |
| Comment | #6272A4 |
| Purple | #BD93F9 |
| Yellow | #F1FA8C |
| Green | #50FA7B |
| Pink | #FF79C6 |
| Red | #FF5555 |
| Orange | #FFB86C |
| Cyan | #8BE9FD |

These eleven values agree between the [official repository](https://github.com/dracula/dracula-theme) and specification. Current Line differs: the current specification assigns #6272A4 as a translucent token; the README lists #44475A. The specified opaque fallback is #353747. [Current Line rendering](https://draculatheme.com/spec#current-line-rendering).

| UI surface | Hex |
|---|---|
| Floating / Background Light | #343746 |
| Background Lighter | #424450 |
| Background Dark | #21222C |
| Background Darker | #191A21 |

| Functional UI token | Hex |
|---|---|
| Red | #DE5735 |
| Orange | #A39514 |
| Green | #089108 |
| Cyan | #0081D6 |
| Purple | #815CD6 |

UI surfaces and functional values come from the [official UI specification](https://draculatheme.com/spec#ui-color-palette).

| ANSI | Normal | Bright |
|---|---|---|
| Black | #21222C | #6272A4 |
| Red | #FF5555 | #FF6E6E |
| Green | #50FA7B | #69FF94 |
| Yellow | #F1FA8C | #FFFFA5 |
| Blue | #BD93F9 | #D6ACFF |
| Magenta | #FF79C6 | #FF92DF |
| Cyan | #8BE9FD | #A4FFFF |
| White | #F8F8F2 | #FFFFFF |

ANSI values are included for completeness from the [official terminal palette](https://draculatheme.com/spec#ansi-color-palette).

For your OLED screen, preserving true black would create a deliberate NullTV adaptation: Dracula's own background is #282A36. The reference image shows exact RGB swatches; perceived appearance can vary with display settings. Cyan is included in this research for completeness.
