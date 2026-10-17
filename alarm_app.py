import os
import json
import time
import datetime
import threading
import winsound
from tkinter import filedialog
import customtkinter as ctk
import pygame

ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")

DATA_FILE = "alarms.json"


class AddAlarmDialog(ctk.CTkToplevel):
    def __init__(self, parent, on_save, existing_alarm=None):
        super().__init__(parent)
        self.on_save = on_save
        self.existing_alarm = existing_alarm
        self.audio_path = existing_alarm.get("audio_path", "") if existing_alarm else ""

        self.title("Edit Alarm" if existing_alarm else "Add New Alarm")
        self.geometry("420x480")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()

        self.update_idletasks()
        x = parent.winfo_x() + (parent.winfo_width() // 2) - 210
        y = parent.winfo_y() + (parent.winfo_height() // 2) - 240
        self.geometry(f"+{x}+{y}")

        self._build_ui()

    def _build_ui(self):
        time_frame = ctk.CTkFrame(self, fg_color="transparent")
        time_frame.pack(pady=(25, 15))

        hours = [f"{h:02d}" for h in range(24)]
        minutes = [f"{m:02d}" for m in range(60)]

        default_h, default_m = "07", "00"
        if self.existing_alarm:
            default_h, default_m = self.existing_alarm["time"].split(":")

        self.hour_var = ctk.StringVar(value=default_h)
        self.min_var = ctk.StringVar(value=default_m)

        self.hour_menu = ctk.CTkOptionMenu(
            time_frame, values=hours, variable=self.hour_var,
            width=90, height=50, font=ctk.CTkFont(size=24, weight="bold")
        )
        self.hour_menu.grid(row=0, column=0, padx=5)

        colon = ctk.CTkLabel(time_frame, text=":", font=ctk.CTkFont(size=32, weight="bold"))
        colon.grid(row=0, column=1, padx=4)

        self.min_menu = ctk.CTkOptionMenu(
            time_frame, values=minutes, variable=self.min_var,
            width=90, height=50, font=ctk.CTkFont(size=24, weight="bold")
        )
        self.min_menu.grid(row=0, column=2, padx=5)

        ctk.CTkLabel(self, text="Alarm Name", font=ctk.CTkFont(size=13, weight="bold")).pack(anchor="w", padx=35)
        self.label_entry = ctk.CTkEntry(self, placeholder_text="Morning Workout, Wake up...", width=350, height=36)
        self.label_entry.pack(pady=(5, 15), padx=35)
        if self.existing_alarm:
            self.label_entry.insert(0, self.existing_alarm.get("label", "Alarm"))
        else:
            self.label_entry.insert(0, "Alarm")

        ctk.CTkLabel(self, text="Repeat Days", font=ctk.CTkFont(size=13, weight="bold")).pack(anchor="w", padx=35)
        days_frame = ctk.CTkFrame(self, fg_color="transparent")
        days_frame.pack(pady=(6, 18), padx=35, fill="x")

        self.day_names = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
        self.day_vars = {}
        saved_days = self.existing_alarm.get("days", self.day_names[:5]) if self.existing_alarm else self.day_names[:5]

        for i, day in enumerate(self.day_names):
            var = ctk.BooleanVar(value=(day in saved_days))
            self.day_vars[day] = var
            cb = ctk.CTkCheckBox(
                days_frame, text=day[:2], variable=var, width=42, checkbox_width=20, checkbox_height=20,
                font=ctk.CTkFont(size=12)
            )
            cb.grid(row=0, column=i, padx=3)

        ctk.CTkLabel(self, text="Custom Alarm Music", font=ctk.CTkFont(size=13, weight="bold")).pack(anchor="w", padx=35)
        audio_frame = ctk.CTkFrame(self, fg_color=("#E5E5E5", "#2B2B2B"), corner_radius=8)
        audio_frame.pack(pady=(6, 25), padx=35, fill="x")

        file_display = os.path.basename(self.audio_path) if self.audio_path else "Default System Beep"
        self.audio_label = ctk.CTkLabel(
            audio_frame, text=file_display, font=ctk.CTkFont(size=12),
            anchor="w", width=210
        )
        self.audio_label.pack(side="left", padx=12, pady=10, fill="x", expand=True)

        browse_btn = ctk.CTkButton(
            audio_frame, text="Browse...", width=90, height=30,
            command=self._choose_audio
        )
        browse_btn.pack(side="right", padx=10, pady=8)

        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.pack(fill="x", padx=35, pady=(10, 0))

        save_btn = ctk.CTkButton(btn_frame, text="Save Alarm", height=38, command=self._save)
        save_btn.pack(side="left", expand=True, fill="x", padx=(0, 6))

        cancel_btn = ctk.CTkButton(
            btn_frame, text="Cancel", height=38,
            fg_color="transparent", border_width=1, text_color=("gray10", "gray90"),
            command=self.destroy
        )
        cancel_btn.pack(side="right", expand=True, fill="x", padx=(6, 0))

    def _choose_audio(self):
        path = filedialog.askopenfilename(
            title="Select Alarm Music File",
            filetypes=[("Audio Files", "*.mp3 *.wav *.ogg *.flac"), ("All Files", "*.*")]
        )
        if path:
            self.audio_path = path
            name = os.path.basename(path)
            self.audio_label.configure(text=name if len(name) < 28 else name[:25] + "...")

    def _save(self):
        selected_days = [d for d, var in self.day_vars.items() if var.get()]
        alarm_data = {
            "id": self.existing_alarm["id"] if self.existing_alarm else int(time.time() * 1000),
            "time": f"{self.hour_var.get()}:{self.min_var.get()}",
            "label": self.label_entry.get().strip() or "Alarm",
            "days": selected_days,
            "audio_path": self.audio_path,
            "enabled": True
        }
        self.on_save(alarm_data)
        self.destroy()


class RingingModal(ctk.CTkToplevel):
    def __init__(self, parent, alarm, on_dismiss, on_snooze):
        super().__init__(parent)
        self.alarm = alarm
        self.on_dismiss = on_dismiss
        self.on_snooze = on_snooze

        self.title("Alarm Ringing!")
        self.geometry("380x260")
        self.resizable(False, False)
        self.attributes("-topmost", True)
        self.protocol("WM_DELETE_WINDOW", self._dismiss)

        self.update_idletasks()
        x = parent.winfo_x() + (parent.winfo_width() // 2) - 190
        y = parent.winfo_y() + (parent.winfo_height() // 2) - 130
        self.geometry(f"+{x}+{y}")

        ctk.CTkLabel(self, text=alarm["time"], font=ctk.CTkFont(size=48, weight="bold")).pack(pady=(25, 5))
        ctk.CTkLabel(self, text=alarm["label"], font=ctk.CTkFont(size=18)).pack(pady=(0, 10))

        song_name = os.path.basename(alarm["audio_path"]) if alarm.get("audio_path") else "Default Alarm Sound"
        ctk.CTkLabel(self, text=f"♪ {song_name}", font=ctk.CTkFont(size=12), text_color="gray70").pack(pady=(0, 20))

        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.pack(fill="x", padx=30)

        snooze_btn = ctk.CTkButton(
            btn_frame, text="Snooze (5m)", height=40, fg_color="#3B3B3B", hover_color="#505050",
            command=self._snooze
        )
        snooze_btn.pack(side="left", expand=True, fill="x", padx=(0, 6))

        dismiss_btn = ctk.CTkButton(
            btn_frame, text="Dismiss", height=40, fg_color="#E81123", hover_color="#C50F1F",
            command=self._dismiss
        )
        dismiss_btn.pack(side="right", expand=True, fill="x", padx=(6, 0))

    def _dismiss(self):
        self.on_dismiss()
        self.destroy()

    def _snooze(self):
        self.on_snooze(self.alarm)
        self.destroy()


class WindowsClockApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("Clock")
        self.geometry("760x540")
        self.minsize(640, 460)

        pygame.mixer.init()

        self.alarms = self._load_alarms()
        self.triggered_this_minute = set()
        self.is_ringing = False

        self.sw_running = False
        self.sw_start_time = 0.0
        self.sw_elapsed = 0.0

        self._build_layout()
        self._tick_clock()

    def _load_alarms(self):
        if os.path.exists(DATA_FILE):
            try:
                with open(DATA_FILE, "r", encoding="utf-8") as f:
                    return json.load(f)
            except Exception:
                return []
        return [
            {
                "id": 1,
                "time": "07:00",
                "label": "Good Morning",
                "days": ["Mon", "Tue", "Wed", "Thu", "Fri"],
                "audio_path": "",
                "enabled": True
            }
        ]

    def _save_alarms(self):
        with open(DATA_FILE, "w", encoding="utf-8") as f:
            json.dump(self.alarms, f, indent=2)

    def _build_layout(self):
        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)

        self.sidebar = ctk.CTkFrame(self, width=190, corner_radius=0, fg_color=("#F3F3F3", "#1F1F1F"))
        self.sidebar.grid(row=0, column=0, sticky="nsew")
        self.sidebar.grid_propagate(False)

        app_title = ctk.CTkLabel(self.sidebar, text="Clock", font=ctk.CTkFont(size=20, weight="bold"))
        app_title.pack(anchor="w", padx=24, pady=(24, 20))

        self.nav_alarms_btn = ctk.CTkButton(
            self.sidebar, text="  ⏰   Alarms", anchor="w", height=40, corner_radius=8,
            font=ctk.CTkFont(size=14, weight="bold"),
            command=lambda: self._switch_view("alarms")
        )
        self.nav_alarms_btn.pack(fill="x", padx=12, pady=4)

        self.nav_sw_btn = ctk.CTkButton(
            self.sidebar, text="  ⏱   Stopwatch", anchor="w", height=40, corner_radius=8,
            font=ctk.CTkFont(size=14), fg_color="transparent", text_color=("gray10", "gray90"),
            command=lambda: self._switch_view("stopwatch")
        )
        self.nav_sw_btn.pack(fill="x", padx=12, pady=4)

        clock_card = ctk.CTkFrame(self.sidebar, fg_color=("#E5E5E5", "#2B2B2B"), corner_radius=10)
        clock_card.pack(side="bottom", fill="x", padx=14, pady=18)

        self.live_time_lbl = ctk.CTkLabel(clock_card, text="00:00:00", font=ctk.CTkFont(size=22, weight="bold"))
        self.live_time_lbl.pack(pady=(12, 2))

        self.live_date_lbl = ctk.CTkLabel(clock_card, text="Monday, Jan 1", font=ctk.CTkFont(size=12), text_color="gray70")
        self.live_date_lbl.pack(pady=(0, 12))

        self.main_container = ctk.CTkFrame(self, fg_color="transparent")
        self.main_container.grid(row=0, column=1, sticky="nsew", padx=28, pady=24)
        self.main_container.grid_columnconfigure(0, weight=1)
        self.main_container.grid_rowconfigure(1, weight=1)

        self.alarms_view = ctk.CTkFrame(self.main_container, fg_color="transparent")
        self.stopwatch_view = ctk.CTkFrame(self.main_container, fg_color="transparent")

        self._build_alarms_view()
        self._build_stopwatch_view()
        self._switch_view("alarms")

    def _build_alarms_view(self):
        header = ctk.CTkFrame(self.alarms_view, fg_color="transparent")
        header.pack(fill="x", pady=(0, 16))

        ctk.CTkLabel(header, text="Alarms", font=ctk.CTkFont(size=26, weight="bold")).pack(side="left")

        add_btn = ctk.CTkButton(
            header, text="+ Add an alarm", width=130, height=36, corner_radius=8,
            font=ctk.CTkFont(size=13, weight="bold"),
            command=self._open_add_dialog
        )
        add_btn.pack(side="right")

        self.alarms_scroll = ctk.CTkScrollableFrame(self.alarms_view, fg_color="transparent")
        self.alarms_scroll.pack(fill="both", expand=True)

        self._render_alarms()

    def _render_alarms(self):
        for widget in self.alarms_scroll.winfo_children():
            widget.destroy()

        if not self.alarms:
            empty = ctk.CTkLabel(
                self.alarms_scroll, text="No alarms set. Click '+ Add an alarm' to get started.",
                font=ctk.CTkFont(size=14), text_color="gray60"
            )
            empty.pack(pady=80)
            return

        for alarm in sorted(self.alarms, key=lambda x: x["time"]):
            card = ctk.CTkFrame(self.alarms_scroll, fg_color=("#FFFFFF", "#262626"), corner_radius=12)
            card.pack(fill="x", pady=7, padx=2)

            left = ctk.CTkFrame(card, fg_color="transparent")
            left.pack(side="left", padx=20, pady=14, fill="both", expand=True)

            time_row = ctk.CTkFrame(left, fg_color="transparent")
            time_row.pack(anchor="w")

            time_lbl = ctk.CTkLabel(
                time_row, text=alarm["time"],
                font=ctk.CTkFont(size=34, weight="bold"),
                text_color=("gray10", "white") if alarm["enabled"] else "gray50"
            )
            time_lbl.pack(side="left", padx=(0, 14))

            name_lbl = ctk.CTkLabel(
                time_row, text=alarm["label"],
                font=ctk.CTkFont(size=15, weight="bold"),
                text_color=("gray20", "gray85") if alarm["enabled"] else "gray50"
            )
            name_lbl.pack(side="left")

            days_text = ", ".join(alarm["days"]) if alarm["days"] else "Once"
            if len(alarm["days"]) == 7:
                days_text = "Every day"
            elif alarm["days"] == ["Mon", "Tue", "Wed", "Thu", "Fri"]:
                days_text = "Weekdays"

            song_name = os.path.basename(alarm["audio_path"]) if alarm.get("audio_path") else "Default Beep"
            meta_lbl = ctk.CTkLabel(
                left, text=f"{days_text}   •   ♪ {song_name}",
                font=ctk.CTkFont(size=12), text_color="gray60"
            )
            meta_lbl.pack(anchor="w", pady=(4, 0))

            right = ctk.CTkFrame(card, fg_color="transparent")
            right.pack(side="right", padx=18)

            switch_var = ctk.BooleanVar(value=alarm["enabled"])
            toggle = ctk.CTkSwitch(
                right, text="", variable=switch_var, width=46,
                command=lambda a=alarm, v=switch_var: self._toggle_alarm(a, v.get())
            )
            toggle.pack(side="left", padx=8)

            edit_btn = ctk.CTkButton(
                right, text="Edit", width=55, height=30, fg_color="#383838", hover_color="#4C4C4C",
                command=lambda a=alarm: self._open_edit_dialog(a)
            )
            edit_btn.pack(side="left", padx=4)

            del_btn = ctk.CTkButton(
                right, text="✕", width=34, height=30, fg_color="transparent",
                hover_color="#C50F1F", text_color="gray70",
                command=lambda a=alarm: self._delete_alarm(a["id"])
            )
            del_btn.pack(side="left", padx=(4, 0))

    def _build_stopwatch_view(self):
        ctk.CTkLabel(self.stopwatch_view, text="Stopwatch", font=ctk.CTkFont(size=26, weight="bold")).pack(anchor="w")

        center = ctk.CTkFrame(self.stopwatch_view, fg_color="transparent")
        center.pack(expand=True)

        self.sw_label = ctk.CTkLabel(center, text="00:00:00.0", font=ctk.CTkFont(size=64, weight="bold"))
        self.sw_label.pack(pady=30)

        btn_row = ctk.CTkFrame(center, fg_color="transparent")
        btn_row.pack()

        self.sw_start_btn = ctk.CTkButton(btn_row, text="Start", width=120, height=42, command=self._toggle_stopwatch)
        self.sw_start_btn.pack(side="left", padx=8)

        self.sw_reset_btn = ctk.CTkButton(
            btn_row, text="Reset", width=120, height=42, fg_color="#383838", hover_color="#4C4C4C",
            command=self._reset_stopwatch
        )
        self.sw_reset_btn.pack(side="left", padx=8)

    def _switch_view(self, view_name):
        self.alarms_view.pack_forget()
        self.stopwatch_view.pack_forget()

        if view_name == "alarms":
            self.alarms_view.pack(fill="both", expand=True)
            self.nav_alarms_btn.configure(fg_color=("#3B8ED0", "#1F6AA5"))
            self.nav_sw_btn.configure(fg_color="transparent")
        else:
            self.stopwatch_view.pack(fill="both", expand=True)
            self.nav_sw_btn.configure(fg_color=("#3B8ED0", "#1F6AA5"))
            self.nav_alarms_btn.configure(fg_color="transparent")

    def _open_add_dialog(self):
        AddAlarmDialog(self, self._save_alarm_callback)

    def _open_edit_dialog(self, alarm):
        AddAlarmDialog(self, self._save_alarm_callback, existing_alarm=alarm)

    def _save_alarm_callback(self, alarm_data):
        existing_idx = next((i for i, a in enumerate(self.alarms) if a["id"] == alarm_data["id"]), None)
        if existing_idx is not None:
            self.alarms[existing_idx] = alarm_data
        else:
            self.alarms.append(alarm_data)
        self._save_alarms()
        self._render_alarms()

    def _toggle_alarm(self, alarm, state):
        alarm["enabled"] = state
        self._save_alarms()
        self._render_alarms()

    def _delete_alarm(self, alarm_id):
        self.alarms = [a for a in self.alarms if a["id"] != alarm_id]
        self._save_alarms()
        self._render_alarms()

    def _trigger_alarm(self, alarm):
        if self.is_ringing:
            return
        self.is_ringing = True

        audio_path = alarm.get("audio_path", "")
        if audio_path and os.path.exists(audio_path):
            try:
                pygame.mixer.music.load(audio_path)
                pygame.mixer.music.play(loops=-1)
            except Exception:
                self._start_fallback_beep()
        else:
            self._start_fallback_beep()

        if not alarm.get("days"):
            alarm["enabled"] = False
            self._save_alarms()
            self._render_alarms()

        RingingModal(self, alarm, self._stop_audio, self._snooze_alarm)

    def _start_fallback_beep(self):
        def beep_loop():
            while self.is_ringing:
                winsound.Beep(880, 400)
                time.sleep(0.3)
        threading.Thread(target=beep_loop, daemon=True).start()

    def _stop_audio(self):
        self.is_ringing = False
        if pygame.mixer.get_init():
            pygame.mixer.music.stop()

    def _snooze_alarm(self, alarm):
        self._stop_audio()
        snooze_time = (datetime.datetime.now() + datetime.timedelta(minutes=5)).strftime("%H:%M")
        snoozed = {
            "id": int(time.time() * 1000),
            "time": snooze_time,
            "label": f"{alarm['label']} (Snoozed)",
            "days": [],
            "audio_path": alarm.get("audio_path", ""),
            "enabled": True
        }
        self.alarms.append(snoozed)
        self._save_alarms()
        self._render_alarms()

    def _toggle_stopwatch(self):
        if not self.sw_running:
            self.sw_running = True
            self.sw_start_time = time.perf_counter() - self.sw_elapsed
            self.sw_start_btn.configure(text="Pause")
            self._tick_stopwatch()
        else:
            self.sw_running = False
            self.sw_start_btn.configure(text="Resume")

    def _reset_stopwatch(self):
        self.sw_running = False
        self.sw_elapsed = 0.0
        self.sw_label.configure(text="00:00:00.0")
        self.sw_start_btn.configure(text="Start")

    def _tick_stopwatch(self):
        if self.sw_running:
            self.sw_elapsed = time.perf_counter() - self.sw_start_time
            mins, rem = divmod(int(self.sw_elapsed), 60)
            hrs, mins = divmod(mins, 60)
            tenths = int((self.sw_elapsed - int(self.sw_elapsed)) * 10)
            self.sw_label.configure(text=f"{hrs:02d}:{mins:02d}:{rem:02d}.{tenths}")
            self.after(50, self._tick_stopwatch)

    def _tick_clock(self):
        now = datetime.datetime.now()
        current_hm = now.strftime("%H:%M")
        current_day = now.strftime("%a")

        self.live_time_lbl.configure(text=now.strftime("%H:%M:%S"))
        self.live_date_lbl.configure(text=now.strftime("%A, %b %d"))

        if now.second == 0:
            for alarm in self.alarms:
                if not alarm["enabled"]:
                    continue
                key = f"{alarm['id']}_{current_hm}"
                if alarm["time"] == current_hm and key not in self.triggered_this_minute:
                    if not alarm["days"] or current_day in alarm["days"]:
                        self.triggered_this_minute.add(key)
                        self._trigger_alarm(alarm)
        else:
            if now.second > 2:
                self.triggered_this_minute.clear()

        self.after(500, self._tick_clock)


if __name__ == "__main__":
    app = WindowsClockApp()
    app.mainloop()
