<img width="1288" height="896" alt="image" src="https://github.com/user-attachments/assets/e3ec432c-8ea7-431c-ba08-386306f17a90" />

# Flint

Control your **Positive Grid Spark 2** guitar amp from your computer, without needing a phone.

Flint talks to the amp over Bluetooth, much like the official Spark app does. It does not replace that app
and unlocks nothing: no tones, no content, no features the amp doesn't already have. It just means you can switch
presets and turn knobs from your desk. If the amp turns off or the connection drops, it reconnects by itself.

> **Unofficial.** Flint is not made by or affiliated with Positive Grid. "Spark" and "Positive Grid" are their
> trademarks. This repository contains none of Positive Grid's code, images or fonts. The app draws its screens with
> artwork taken from **your own copy** of the Spark Android app (see [Setup](#setup)).

## What it does

- **Tone:** pick presets, change amps and pedals, turn every knob, set the EQ, switch effects on and off, and save
  presets to the amp's slots.
- **Preset library:** presets live on your computer as well as on the amp, so you can export, import and back them up.
- **Tuner** and the amp's **Simple Looper**.
- **Amp settings:** name, auto standby and shutdown, line-out mute.
- Stays in the system tray and can start when you log in.

**Platforms:** Linux. Windows is not supported yet.

## Setup

You need a Linux PC with Bluetooth, the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build Flint, and
an Android phone with the Spark app on it.

**1. Build and install:**

```bash
./build-linux.sh --install
```

This puts the app in `~/.local/bin/Flint`. The first time it starts, **Flint** is added to your
application menu.

**2. Start it and follow the setup screen.** Flint looks like the Spark app because it uses the app's own
images, fonts and panel layouts, which it takes from the Spark app on your phone. The first time it starts, the setup
screen walks you through it: turning on USB debugging on the phone, connecting it with a USB cable, and allowing this
computer. Each step shows what it's doing, and if something is in the way (Java missing, the phone not allowed yet,
the Spark app not installed), it says what to do and carries on by itself once that's done. It takes a couple of
minutes, then Flint restarts with the artwork.

Nothing on the phone changes, and nothing leaves this computer. Setup needs Java 11 or newer and tells you how to
install it if it's missing; it downloads Google's adb and [jadx](https://github.com/skylot/jadx) itself if you don't
have them. You can turn USB debugging off again afterwards.

No Android phone? The setup screen also takes the Spark app as an `.apkm` file.

When the Spark app updates with new amps or effects, right-click the tray icon and choose **Update Spark artwork…**.

## Using it

1. Turn on the amp. **Only one device can be connected to it at a time**, so turn off Bluetooth on your phone or
   quit the Spark app.
2. Start **Flint**. It finds the amp and connects; nothing needs pairing.
3. Closing the window leaves the app running in the tray. Right-click the tray icon for **Start at login** and
   **Quit**. Started at login, it waits in the tray; started any other way, its window opens. GNOME shows a tray icon only with the AppIndicator extension. Without it, closing the window quits.

**Presets** are kept in `~/.local/share/Flint/presets/`, in the same JSON format the Spark app uses. On the
Tone screen, the ⋮ menu has **Export preset…** and **Import presets…**. To back up your library, copy that folder.

## Troubleshooting

**The amp is never found.**

- Check that Bluetooth Low Energy is on. If `/etc/bluetooth/main.conf` has `ControllerMode = bredr`, change it to
  `dual` (the default) or `le`, then run `sudo systemctl restart bluetooth`.
- Make sure no phone or other device is connected to the amp.

**Access denied.** If the log shows `org.freedesktop.DBus.Error.AccessDenied`, your distribution limits Bluetooth to
a user group. Add yourself to that group and log in again.

**Logs** are written to `~/.local/share/Flint/logs/`, one file per day, kept for 14 days. To keep them
somewhere else, set `FLINT_LOG_DIR`. Include the log when you report a problem.

## License

Flint's own code is under the [MIT License](LICENSE). The Spark app's artwork that Flint draws with belongs to
Positive Grid; it is never part of this repository, and each user takes it from their own copy of the app.
