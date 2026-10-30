// Copyright (c) OPC Foundation. Licensed under the MIT License.
// Transparent build-only launcher; not linked into the native library.
use std::{env, fs, process};

fn main() -> std::io::Result<()> {
    let linker = env::var_os("OPCUA_HTTP3_LINKER").expect("pinned linker required");
    let root = std::path::PathBuf::from(
        env::var_os("OPCUA_HTTP3_LINK_LOG").expect("owned receipt directory required"),
    );
    let args: Vec<_> = env::args_os().skip(1).collect();
    let stem = format!("link-{}", process::id());
    let mut command = linker.to_string_lossy().into_owned();
    for arg in &args {
        command.push('\n');
        command.push_str(&arg.to_string_lossy());
    }
    fs::write(root.join(format!("{stem}.args.txt")), command)?;
    let log_path = root.join(format!("{stem}.output.txt"));
    let log = fs::File::create(&log_path)?;
    let status = process::Command::new(linker)
        .args(args)
        .stdout(log.try_clone()?)
        .stderr(log)
        .status()?;
    if !status.success() {
        eprint!("{}", fs::read_to_string(log_path).unwrap_or_default());
    } else {
        // MSVC's import-library creation message is informational. Keep the full receipt
        // and forward every real warning/error unchanged rather than suppressing diagnostics.
        for line in fs::read_to_string(log_path)?.lines() {
            let lower = line.to_ascii_lowercase();
            if lower.contains(": warning ") || lower.contains(": error ") || lower.contains("fatal error") {
                eprintln!("{line}");
            }
        }
    }
    process::exit(status.code().unwrap_or(1));
}
