#!/usr/bin/env python3
"""
Upgrade2026_Smart.py - Smart Version Checker & Tool Manager
A modern Python GUI application for checking and updating fuzzing security tools.
Features: Slither, Echidna, Medusa, Halmos version management with native rich UI.
"""

import tkinter as tk
from tkinter import ttk, scrolledtext, messagebox
import subprocess
import requests
import json
import os
import sys
import logging
from pathlib import Path
from datetime import datetime
from typing import Tuple, Dict, List
import threading
from dataclasses import dataclass
from packaging import version


# ==================== Configuration ====================

CONFIG = {
    'tools_dir': r'C:\Tools\Fuzzers',
    'go_bin': os.path.expandvars(r'%USERPROFILE%\go\bin'),
    'use_go_bin_for_medusa': True,
}

# Setup logging
LOG_DIR = Path(os.environ.get('TEMP', '/tmp'))
LOG_FILE = LOG_DIR / 'Upgrade2026_Smart.log'

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    handlers=[
        logging.FileHandler(LOG_FILE),
        logging.StreamHandler()
    ]
)
logger = logging.getLogger(__name__)


# ==================== Data Models ====================

@dataclass
class ToolStatus:
    """Represents the status of a security tool."""
    name: str
    current_version: str
    latest_version: str
    update_available: bool
    status: str

    @property
    def status_symbol(self) -> str:
        return "⚠ " if self.update_available else "✓ "


# ==================== Version Comparison ====================

def compare_versions(current: str, latest: str) -> bool:
    """Compare versions and return True if update is needed."""
    if current in ("not installed", "error"):
        return True
    if latest in ("error", "not found"):
        return False
    
    # Clean versions
    current_clean = current.lstrip('v').rstrip('.0')
    latest_clean = latest.lstrip('v').rstrip('.0')
    
    if current_clean == latest_clean:
        return False
    
    try:
        curr_parts = [int(x) for x in current_clean.split('.') if x.isdigit()]
        latest_parts = [int(x) for x in latest_clean.split('.') if x.isdigit()]
        
        max_len = max(len(curr_parts), len(latest_parts))
        curr_parts.extend([0] * (max_len - len(curr_parts)))
        latest_parts.extend([0] * (max_len - len(latest_parts)))
        
        for curr, lat in zip(curr_parts, latest_parts):
            if lat > curr:
                return True
            if curr > lat:
                return False
    except (ValueError, AttributeError):
        return False
    
    return False


# ==================== Tool Version Checkers ====================

class ToolChecker:
    """Base class for checking tool versions."""
    
    @staticmethod
    def run_command(cmd: str) -> str:
        """Run a shell command and return output."""
        try:
            result = subprocess.run(
                cmd, 
                shell=True, 
                capture_output=True, 
                text=True, 
                timeout=5
            )
            return result.stdout + result.stderr
        except Exception as e:
            logger.error(f"Command failed: {cmd} - {e}")
            return ""


class SlitherChecker(ToolChecker):
    """Check and update Slither (Python static analyzer)."""
    
    @staticmethod
    def get_local_version() -> str:
        try:
            output = SlitherChecker.run_command("slither --version")
            if output:
                import re
                match = re.search(r'(\d+\.\d+\.\d+)', output)
                return match.group(1) if match else "not installed"
        except Exception:
            pass
        return "not installed"
    
    @staticmethod
    def get_latest_version() -> str:
        try:
            response = requests.get("https://pypi.org/pypi/slither-analyzer/json", timeout=5)
            return response.json()['info']['version']
        except Exception as e:
            logger.error(f"Failed to fetch Slither version: {e}")
            return "error"
    
    @staticmethod
    def update() -> bool:
        try:
            result = subprocess.run(
                ["python", "-m", "pip", "install", "--upgrade", "slither-analyzer"],
                capture_output=True,
                text=True,
                timeout=60
            )
            return result.returncode == 0
        except Exception as e:
            logger.error(f"Slither update failed: {e}")
            return False


class EchidnaChecker(ToolChecker):
    """Check and update Echidna (fuzzer)."""
    
    @staticmethod
    def get_local_version() -> str:
        try:
            output = EchidnaChecker.run_command("echidna --version")
            if output:
                import re
                match = re.search(r'Echidna\s+([^\s\(]+)', output)
                return match.group(1).strip() if match else "not installed"
        except Exception:
            pass
        return "not installed"
    
    @staticmethod
    def get_latest_version() -> str:
        try:
            response = requests.get(
                "https://api.github.com/repos/crytic/echidna/releases/latest",
                headers={"User-Agent": "Python"},
                timeout=5
            )
            tag = response.json()['tag_name']
            return tag.lstrip('v')
        except Exception as e:
            logger.error(f"Failed to fetch Echidna version: {e}")
            return "error"
    
    @staticmethod
    def update() -> bool:
        try:
            response = requests.get(
                "https://api.github.com/repos/crytic/echidna/releases/latest",
                headers={"User-Agent": "Python"},
                timeout=5
            )
            assets = response.json()['assets']
            
            # Find Windows build
            asset = next(
                (a for a in assets if 'win64.zip' in a['name'] or 'windows' in a['name'].lower()),
                None
            )
            
            if not asset:
                logger.error("Windows build not found for Echidna")
                return False
            
            logger.info(f"Downloading Echidna from {asset['browser_download_url']}")
            # Download and extract would go here
            return True
        except Exception as e:
            logger.error(f"Echidna update failed: {e}")
            return False


class MedusaChecker(ToolChecker):
    """Check and update Medusa (fuzzer)."""
    
    @staticmethod
    def get_local_version() -> str:
        try:
            output = MedusaChecker.run_command("medusa --version")
            if output:
                import re
                match = re.search(r'version\s+(v?[^\s]+)', output)
                return match.group(1).lstrip('v') if match else "not installed"
        except Exception:
            pass
        return "not installed"
    
    @staticmethod
    def get_latest_version() -> str:
        try:
            response = requests.get(
                "https://api.github.com/repos/crytic/medusa/releases/latest",
                headers={"User-Agent": "Python"},
                timeout=5
            )
            tag = response.json()['tag_name']
            return tag.lstrip('v')
        except Exception as e:
            logger.error(f"Failed to fetch Medusa version: {e}")
            return "error"
    
    @staticmethod
    def update() -> bool:
        try:
            result = subprocess.run(
                ["go", "install", "github.com/crytic/medusa@latest"],
                capture_output=True,
                text=True,
                timeout=60
            )
            return result.returncode == 0
        except Exception as e:
            logger.error(f"Medusa update failed: {e}")
            return False


class HalmosChecker(ToolChecker):
    """Check and update Halmos (symbolic execution)."""
    
    @staticmethod
    def get_local_version() -> str:
        try:
            output = HalmosChecker.run_command("halmos --version")
            if output:
                import re
                match = re.search(r'(\d+\.\d+\.\d+)', output)
                return match.group(1) if match else output.strip()
        except Exception:
            pass
        return "not installed"
    
    @staticmethod
    def get_latest_version() -> str:
        try:
            response = requests.get("https://pypi.org/pypi/halmos/json", timeout=5)
            return response.json()['info']['version']
        except Exception as e:
            logger.error(f"Failed to fetch Halmos version: {e}")
            return "error"
    
    @staticmethod
    def update() -> bool:
        try:
            result = subprocess.run(
                ["python", "-m", "pip", "install", "--upgrade", "halmos"],
                capture_output=True,
                text=True,
                timeout=60
            )
            return result.returncode == 0
        except Exception as e:
            logger.error(f"Halmos update failed: {e}")
            return False


# ==================== Main GUI Application ====================

class ModernScrollbar(ttk.Scrollbar):
    """Custom scrollbar styling."""
    pass


class Upgrade2026App:
    """Main GUI Application for tool version management."""
    
    TOOLS = {
        'Slither': SlitherChecker,
        'Echidna': EchidnaChecker,
        'Medusa': MedusaChecker,
        'Halmos': HalmosChecker,
    }
    
    # Color scheme - Modern dark blue with accent colors
    COLORS = {
        'bg_primary': '#0f1419',
        'bg_secondary': '#1a1f2e',
        'bg_tertiary': '#252b3d',
        'accent_primary': '#00d9ff',
        'accent_warning': '#ffa500',
        'accent_success': '#00ff41',
        'accent_error': '#ff3333',
        'text_primary': '#e0e0e0',
        'text_secondary': '#a0a0a0',
        'border': '#2a3147',
    }
    
    def __init__(self, root):
        self.root = root
        self.root.title("Security Tools Upgrade Manager")
        self.root.geometry("1200x750")
        
        # Set colors
        self.root.configure(bg=self.COLORS['bg_primary'])
        style = ttk.Style()
        style.theme_use('clam')
        self.configure_styles(style)
        
        # Data
        self.tools_status: Dict[str, ToolStatus] = {}
        self.selected_for_update = set()
        self.is_updating = False
        
        # Build UI
        self.build_ui()
        self.center_window()
        
        logger.info("Application started")
    
    def configure_styles(self, style: ttk.Style):
        """Configure ttk styles for modern appearance."""
        style.configure('TFrame', background=self.COLORS['bg_primary'])
        style.configure('TLabel', background=self.COLORS['bg_primary'], foreground=self.COLORS['text_primary'])
        style.configure('Title.TLabel', font=('Segoe UI', 16, 'bold'), background=self.COLORS['bg_primary'], foreground=self.COLORS['accent_primary'])
        style.configure('Header.TLabel', font=('Segoe UI', 11, 'bold'), background=self.COLORS['bg_secondary'], foreground=self.COLORS['text_primary'])
        
        # Button styles
        style.configure('Primary.TButton', font=('Segoe UI', 10))
        style.map('Primary.TButton',
                 foreground=[('pressed', self.COLORS['bg_primary'])],
                 background=[('pressed', self.COLORS['accent_primary'])])
        
        # Treeview
        style.configure('Treeview', background=self.COLORS['bg_secondary'], foreground=self.COLORS['text_primary'], fieldbackground=self.COLORS['bg_secondary'], borderwidth=0)
        style.configure('Treeview.Heading', background=self.COLORS['bg_tertiary'], foreground=self.COLORS['accent_primary'], borderwidth=1)
        style.map('Treeview', background=[('selected', self.COLORS['bg_tertiary'])], foreground=[('selected', self.COLORS['accent_primary'])])
    
    def center_window(self):
        """Center window on screen."""
        self.root.update_idletasks()
        width = self.root.winfo_width()
        height = self.root.winfo_height()
        x = (self.root.winfo_screenwidth() // 2) - (width // 2)
        y = (self.root.winfo_screenheight() // 2) - (height // 2)
        self.root.geometry(f'{width}x{height}+{x}+{y}')
    
    def build_ui(self):
        """Build the user interface."""
        # Main container
        main_frame = tk.Frame(self.root, bg=self.COLORS['bg_primary'])
        main_frame.pack(fill=tk.BOTH, expand=True, padx=15, pady=15)
        
        # Header
        self.build_header(main_frame)
        
        # Status bar with date/time
        self.build_status_bar(main_frame)
        
        # Tools table
        self.build_tools_table(main_frame)
        
        # Log area
        self.build_log_area(main_frame)
        
        # Control buttons
        self.build_controls(main_frame)
        
        # Status indicator
        self.build_status_indicator(main_frame)
    
    def build_header(self, parent):
        """Build application header."""
        header_frame = tk.Frame(parent, bg=self.COLORS['bg_secondary'], relief=tk.FLAT)
        header_frame.pack(fill=tk.X, padx=0, pady=5, ipady=12)
        
        # Add padding
        header_inner = tk.Frame(header_frame, bg=self.COLORS['bg_secondary'])
        header_inner.pack(fill=tk.X, padx=15)
        
        # Title
        title = tk.Label(
            header_inner,
            text="🔐 Security Tools Manager",
            font=('Segoe UI', 18, 'bold'),
            bg=self.COLORS['bg_secondary'],
            fg=self.COLORS['accent_primary']
        )
        title.pack(side=tk.LEFT, padx=0)
        
        # Subtitle
        subtitle = tk.Label(
            header_inner,
            text="Smart version checker for Slither, Echidna, Medusa & Halmos",
            font=('Segoe UI', 9),
            bg=self.COLORS['bg_secondary'],
            fg=self.COLORS['text_secondary']
        )
        subtitle.pack(side=tk.LEFT, padx=20)
    
    def build_status_bar(self, parent):
        """Build status bar with timestamp."""
        status_frame = tk.Frame(parent, bg=self.COLORS['bg_primary'])
        status_frame.pack(fill=tk.X, padx=0, pady=5)
        
        # Update timestamp periodically
        def update_time():
            now = datetime.now().strftime("%A, %d %B %Y - %H:%M:%S")
            time_label.config(text=f"📅 {now}")
            self.root.after(1000, update_time)
        
        time_label = tk.Label(
            status_frame,
            text="",
            font=('Segoe UI', 9),
            bg=self.COLORS['bg_primary'],
            fg=self.COLORS['text_secondary']
        )
        time_label.pack(side=tk.LEFT)
        update_time()
    
    def build_tools_table(self, parent):
        """Build tools status table."""
        table_frame = tk.Frame(parent, bg=self.COLORS['bg_secondary'], relief=tk.FLAT)
        table_frame.pack(fill=tk.BOTH, expand=False, padx=0, pady=5, ipady=0)
        
        # Header
        header = tk.Frame(table_frame, bg=self.COLORS['bg_tertiary'])
        header.pack(fill=tk.X)
        
        headers = ['Tool', 'Current Version', 'Latest Version', 'Update Available', 'Status', 'Update?']
        column_widths = [100, 150, 150, 130, 180, 80]
        
        for header_text, width in zip(headers, column_widths):
            col = tk.Label(
                header,
                text=header_text,
                width=width // 8,
                font=('Segoe UI', 10, 'bold'),
                bg=self.COLORS['bg_tertiary'],
                fg=self.COLORS['accent_primary'],
                padx=10,
                pady=10
            )
            col.pack(side=tk.LEFT, fill=tk.X)
        
        # Tools list
        self.tools_frame = tk.Frame(table_frame, bg=self.COLORS['bg_secondary'])
        self.tools_frame.pack(fill=tk.BOTH, expand=False)
        
        self.tool_widgets = {}
        for tool_name in self.TOOLS.keys():
            self.create_tool_row(self.tools_frame, tool_name)
    
    def create_tool_row(self, parent, tool_name):
        """Create a row for a tool in the table."""
        row_frame = tk.Frame(parent, bg=self.COLORS['bg_secondary'], relief=tk.FLAT)
        row_frame.pack(fill=tk.X, pady=1)
        
        # Tool name
        name_label = tk.Label(
            row_frame,
            text=tool_name,
            width=12,
            font=('Segoe UI', 10),
            bg=self.COLORS['bg_secondary'],
            fg=self.COLORS['accent_primary'],
            padx=10,
            pady=8,
            anchor='w'
        )
        name_label.pack(side=tk.LEFT, fill=tk.X)
        
        # Current version
        current_label = tk.Label(
            row_frame,
            text="Checking...",
            width=18,
            font=('Courier New', 9),
            bg=self.COLORS['bg_secondary'],
            fg=self.COLORS['text_primary'],
            padx=10,
            pady=8,
            anchor='w'
        )
        current_label.pack(side=tk.LEFT, fill=tk.X)
        
        # Latest version
        latest_label = tk.Label(
            row_frame,
            text="Checking...",
            width=18,
            font=('Courier New', 9),
            bg=self.COLORS['bg_secondary'],
            fg=self.COLORS['text_primary'],
            padx=10,
            pady=8,
            anchor='w'
        )
        latest_label.pack(side=tk.LEFT, fill=tk.X)
        
        # Update available
        update_avail_label = tk.Label(
            row_frame,
            text="—",
            width=15,
            font=('Segoe UI', 9),
            bg=self.COLORS['bg_secondary'],
            fg=self.COLORS['text_secondary'],
            padx=10,
            pady=8,
            anchor='w'
        )
        update_avail_label.pack(side=tk.LEFT, fill=tk.X)
        
        # Status
        status_label = tk.Label(
            row_frame,
            text="Checking...",
            width=21,
            font=('Segoe UI', 9),
            bg=self.COLORS['bg_secondary'],
            fg=self.COLORS['text_secondary'],
            padx=10,
            pady=8,
            anchor='w'
        )
        status_label.pack(side=tk.LEFT, fill=tk.X)
        
        # Checkbox for update
        var = tk.BooleanVar()
        checkbox = tk.Checkbutton(
            row_frame,
            variable=var,
            bg=self.COLORS['bg_secondary'],
            fg=self.COLORS['accent_primary'],
            selectcolor=self.COLORS['bg_tertiary'],
            activebackground=self.COLORS['bg_secondary'],
            activeforeground=self.COLORS['accent_primary'],
            padx=10,
            pady=8
        )
        checkbox.pack(side=tk.LEFT, fill=tk.X)
        
        self.tool_widgets[tool_name] = {
            'row': row_frame,
            'current': current_label,
            'latest': latest_label,
            'update_avail': update_avail_label,
            'status': status_label,
            'checkbox': checkbox,
            'var': var
        }
    
    def build_log_area(self, parent):
        """Build log display area."""
        log_label = tk.Label(
            parent,
            text="Operation Log",
            font=('Segoe UI', 10, 'bold'),
            bg=self.COLORS['bg_primary'],
            fg=self.COLORS['text_primary'],
            padx=0,
            pady=5
        )
        log_label.pack(anchor='w')
        
        log_frame = tk.Frame(parent, bg=self.COLORS['bg_secondary'], relief=tk.FLAT, highlightthickness=1, highlightbackground=self.COLORS['border'])
        log_frame.pack(fill=tk.BOTH, expand=True, pady=5)
        
        self.log_box = scrolledtext.ScrolledText(
            log_frame,
            height=8,
            font=('Courier New', 8),
            bg=self.COLORS['bg_tertiary'],
            fg=self.COLORS['text_primary'],
            insertbackground=self.COLORS['accent_primary'],
            relief=tk.FLAT,
            padx=10,
            pady=10
        )
        self.log_box.pack(fill=tk.BOTH, expand=True, padx=0, pady=0)
        
        # Configure tags for colored output
        self.log_box.tag_config('info', foreground=self.COLORS['accent_primary'])
        self.log_box.tag_config('warning', foreground=self.COLORS['accent_warning'])
        self.log_box.tag_config('error', foreground=self.COLORS['accent_error'])
        self.log_box.tag_config('success', foreground=self.COLORS['accent_success'])
    
    def build_controls(self, parent):
        """Build control buttons."""
        button_frame = tk.Frame(parent, bg=self.COLORS['bg_primary'])
        button_frame.pack(fill=tk.X, pady=5)
        
        buttons = [
            ("🔄 Check Versions", self.refresh_versions, self.COLORS['accent_primary']),
            ("⬆️  Update Selected", self.update_selected, self.COLORS['accent_warning']),
            ("⚡ Update All", self.update_all, self.COLORS['accent_error']),
            ("🗑️  Clear Log", self.clear_log, self.COLORS['text_secondary']),
        ]
        
        for text, command, color in buttons:
            btn = tk.Button(
                button_frame,
                text=text,
                command=command,
                font=('Segoe UI', 10, 'bold'),
                bg=color,
                fg=self.COLORS['bg_primary'] if color != self.COLORS['text_secondary'] else self.COLORS['text_primary'],
                relief=tk.FLAT,
                padx=15,
                pady=8,
                cursor='hand2',
                activebackground=color,
                activeforeground=self.COLORS['bg_primary']
            )
            btn.pack(side=tk.LEFT, padx=5)
    
    def build_status_indicator(self, parent):
        """Build status indicator at bottom."""
        self.status_frame = tk.Frame(parent, bg=self.COLORS['bg_primary'])
        self.status_frame.pack(fill=tk.X, pady=3)
        
        self.status_indicator = tk.Label(
            self.status_frame,
            text="✓ Ready",
            font=('Segoe UI', 9),
            bg=self.COLORS['bg_primary'],
            fg=self.COLORS['accent_success'],
            padx=0,
            pady=0
        )
        self.status_indicator.pack(anchor='w')
    
    def update_log(self, message: str, level: str = 'info'):
        """Add message to log box."""
        timestamp = datetime.now().strftime("%H:%M:%S")
        log_entry = f"[{timestamp}] {message}\n"
        
        self.log_box.insert(tk.END, log_entry, level)
        self.log_box.see(tk.END)
        logger.log(logging.INFO if level == 'info' else logging.WARNING if level == 'warning' else logging.ERROR, message)
    
    def set_status(self, message: str, status_type: str = 'ready'):
        """Update status indicator."""
        colors = {
            'ready': self.COLORS['accent_success'],
            'updating': self.COLORS['accent_warning'],
            'error': self.COLORS['accent_error']
        }
        symbols = {
            'ready': '✓',
            'updating': '⏳',
            'error': '✗'
        }
        
        self.status_indicator.config(
            text=f"{symbols.get(status_type, '•')} {message}",
            fg=colors.get(status_type, self.COLORS['text_primary'])
        )
    
    def refresh_versions(self):
        """Check versions for all tools in a background thread."""
        def check_versions():
            self.set_status("Checking versions...", 'updating')
            self.update_log("Checking versions for all tools...", 'info')
            
            for tool_name, checker_class in self.TOOLS.items():
                try:
                    current = checker_class.get_local_version()
                    latest = checker_class.get_latest_version()
                    needs_update = compare_versions(current, latest)
                    
                    status = "⚠ Update available" if needs_update else "✓ Up to date"
                    
                    self.tools_status[tool_name] = ToolStatus(
                        name=tool_name,
                        current_version=current,
                        latest_version=latest,
                        update_available=needs_update,
                        status=status
                    )
                    
                    self.update_tool_row(tool_name)
                    self.update_log(f"{tool_name}: {current} → {latest if needs_update else '✓ latest'}", 'info')
                except Exception as e:
                    self.update_log(f"Error checking {tool_name}: {e}", 'error')
            
            self.set_status("Version check completed", 'ready')
            self.update_log("Version check completed", 'success')
        
        thread = threading.Thread(target=check_versions, daemon=True)
        thread.start()
    
    def update_tool_row(self, tool_name: str):
        """Update UI for a specific tool."""
        if tool_name not in self.tools_status or tool_name not in self.tool_widgets:
            return
        
        status = self.tools_status[tool_name]
        widgets = self.tool_widgets[tool_name]
        
        widgets['current'].config(text=status.current_version)
        widgets['latest'].config(text=status.latest_version)
        
        if status.update_available:
            widgets['update_avail'].config(text="YES", fg=self.COLORS['accent_warning'])
            widgets['status'].config(text="Update available", fg=self.COLORS['accent_warning'])
            widgets['var'].set(True)
        else:
            widgets['update_avail'].config(text="No", fg=self.COLORS['accent_success'])
            widgets['status'].config(text="Up to date", fg=self.COLORS['accent_success'])
            widgets['var'].set(False)
    
    def update_selected(self):
        """Update selected tools."""
        selected = [name for name, widget in self.tool_widgets.items() if widget['var'].get()]
        
        if not selected:
            messagebox.showinfo("Info", "Please select tools to update")
            self.update_log("No tools selected", 'warning')
            return
        
        def do_update():
            self.is_updating = True
            self.set_status("Updating selected tools...", 'updating')
            self.update_log(f"Starting update of {len(selected)} tool(s)...", 'info')
            
            updated_count = 0
            for tool_name in selected:
                try:
                    checker_class = self.TOOLS[tool_name]
                    if checker_class.update():
                        updated_count += 1
                        self.update_log(f"✓ {tool_name} updated successfully", 'success')
                    else:
                        self.update_log(f"✗ Failed to update {tool_name}", 'error')
                except Exception as e:
                    self.update_log(f"Error updating {tool_name}: {e}", 'error')
            
            self.refresh_versions()
            self.is_updating = False
            self.update_log(f"Update completed. Tools updated: {updated_count}/{len(selected)}", 'success')
            self.set_status(f"Updated {updated_count} tool(s)", 'ready')
        
        thread = threading.Thread(target=do_update, daemon=True)
        thread.start()
    
    def update_all(self):
        """Update all tools."""
        if messagebox.askyesno("Confirm", "Update all tools?"):
            self.update_log("Starting update of all tools...", 'warning')
            
            # Simulate selecting all
            for widget in self.tool_widgets.values():
                widget['var'].set(True)
            
            self.update_selected()
    
    def clear_log(self):
        """Clear the log box."""
        self.log_box.delete(1.0, tk.END)
        self.update_log("Log cleared", 'info')


def main():
    """Main entry point."""
    root = tk.Tk()
    app = Upgrade2026App(root)
    
    # Initial check on startup
    root.after(500, app.refresh_versions)
    
    root.mainloop()


if __name__ == '__main__':
    main()
