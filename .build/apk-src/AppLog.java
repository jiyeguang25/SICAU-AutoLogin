package cn.edu.sicau.autologin;

import java.io.File;
import java.io.FileWriter;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class AppLog {
    public interface Listener { void onLog(String line); }

    private static Listener listener;
    private static File logFile;
    private static final List<String> recent = new ArrayList<String>();
    private static final SimpleDateFormat FMT = new SimpleDateFormat("MM-dd HH:mm:ss", Locale.US);

    public static void init(File dir) {
        if (dir != null) logFile = new File(dir, "autologin.log");
    }

    public static void setListener(Listener l) { listener = l; }

    public static List<String> recent() {
        synchronized (recent) { return new ArrayList<String>(recent); }
    }

    public static void i(String msg) { write("INFO", msg); }

    public static void write(String level, String msg) {
        String line = FMT.format(new Date()) + " [" + level + "] " + msg;
        synchronized (recent) {
            recent.add(line);
            while (recent.size() > 300) recent.remove(0);
        }
        Listener l = listener;
        if (l != null) { try { l.onLog(line); } catch (Exception e) { } }
        if (logFile != null) {
            try {
                if (logFile.length() > 256 * 1024) logFile.delete();
                FileWriter w = new FileWriter(logFile, true);
                w.write(line + "\n");
                w.close();
            } catch (Exception e) { }
        }
    }
}
