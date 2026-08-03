# Change Log

## 8/3/2026

- Added `run` and `build` scripts to make new installs easy
  - build will auto-install needed 3rd party tools
  - will auto detect where DW2 is installed for both Steam and GOG versions
  - allows user to override specific install to use
  - prepopulates the resulting tool with this information
  - run will automatically capture the full output log to `LastRun.log` for reference
- Added high DPI aware init
- Tweaked dialog layouts for high DPI displays
- Improved external tool calls to better avoid hang conditions
- Added user-controllable timeouts for external tools so that they cannot hang forever
- Improved failure detection and reporting so that a list of failures is presented in the summary at the end
- Defaults to using this repo's Output folder for extracted data (user overridable each time)
