# Security policy

## Supported versions

Only the latest release gets security fixes. Installed copies update
themselves, so the fix reaches people once a new release is out.

| Version | Supported |
| --- | --- |
| Latest release | Yes |
| Anything older | No, please update |

## Reporting a vulnerability

Please don't report security problems in a public issue or discussion.

Use **[Report a vulnerability](https://github.com/yyyutakaaa/Muted/security/advisories/new)**
on the Security tab instead. Only the maintainer can see the report.

Include what you can of:

- what the problem is and what an attacker could do with it;
- the steps or a proof of concept to reproduce it;
- the Muted version and Windows version you tested on.

You can expect a first reply within a week. Once the problem is confirmed, a
fixed release goes out as soon as possible, and you are credited in the release
notes unless you'd rather not be.

## What is in scope

Muted runs locally and sends no audio anywhere. The parts most worth looking at:

- **The updater**: it downloads the installer from GitHub releases, checks it
  against the published SHA-256 checksum and runs it.
- **Command-line commands**: a second `Muted.exe` passes commands such as
  `--toggle-mute` to the copy that is already running.
- **Settings and profile import**: JSON files read from disk or imported by the
  user.
- **The installer and the bundled `rnnoise.dll`.**

Problems in the virtual audio cable, Windows or third-party drivers are out of
scope; report those to their makers.
