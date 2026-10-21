# dmgbuild settings for Hanglish.dmg: a drag-to-Applications window on a custom background.
# Called from bundle.sh with -D app=... -D background=... -D icon=...
# The icon positions match the rings drawn in dmg-background.svg.

app = defines["app"]

files = [app]
symlinks = {"Applications": "/Applications"}
hide_extensions = ["Hanglish.app"]

icon = defines["icon"]              # the mounted disk shows the app icon
background = defines["background"]  # a HiDPI TIFF holding the 1x and 2x images

format = "UDZO"
filesystem = "HFS+"

window_rect = ((200, 140), (660, 400))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False

icon_size = 128
text_size = 13
icon_locations = {
    "Hanglish.app": (165, 205),
    "Applications": (495, 205),
}
