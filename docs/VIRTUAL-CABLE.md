# Setting up a virtual audio cable

A normal Windows app can't publish a new microphone. Muted sends your cleaned
voice to the playback side of a signed virtual cable that you have installed,
and your call app uses the cable's recording side as its microphone.

## VB-CABLE

1. Download the current package from the official
   [VB-CABLE page](https://vb-audio.com/Cable/).
2. Extract the whole archive and run the x64 setup as administrator.
3. Restart Windows if the installer asks for it.
4. In Muted, pick your real microphone under **Microphone**.
5. Pick `CABLE Input` or `CABLE In 16ch` under **Cable output**.
6. Start Muted with the power button.
7. In Discord, Teams, Zoom or your game, select `CABLE Output` or
   `CABLE Out 16ch` as the microphone. Leave the call app's output on your
   headset or speakers.

## The 48 kHz requirement

Muted processes audio at 48 kHz. Set both ends of the cable to 48,000 Hz in
Windows: **Settings → System → Sound → More sound settings**, then open the
cable on the **Playback** and on the **Recording** tab, **Properties →
Advanced**. The setup check in Muted tells you when a device runs at another
rate.

If you use echo cancellation, the output you listen on has to run at 48 kHz
too.

## Tips

- Turn off your call app's own noise suppression first. Two suppressors on top
  of each other can make your voice pump or sound distorted.
- If the other side hears themselves through your speakers, switch on **Echo
  cancellation** and pick the output you actually listen to.

## Troubleshooting

- Check **Settings → Privacy & security → Microphone** and allow desktop apps
  to use the microphone.
- Restart your call app if you installed the cable after starting it.
- Muted only starts into a recognised virtual cable. That stops it from
  feeding your own sound back into your microphone when a saved cable is
  missing and Windows falls back to your speakers.
- Settings are stored in `%LOCALAPPDATA%\Muted\settings.json`. Delete that
  file while Muted is closed to reset everything.
- Errors are logged to `%LOCALAPPDATA%\Muted\Muted.log`.
- Still stuck? Use **Settings → Check setup → Report a problem**, or ask in
  [Discussions](https://github.com/yyyutakaaa/Muted/discussions).

The cable driver is not bundled with Muted and falls under its vendor's
license terms.
