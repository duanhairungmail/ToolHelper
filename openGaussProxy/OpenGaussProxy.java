import com.sun.net.httpserver.*;
import java.io.*;
import java.net.InetSocketAddress;
import java.sql.*;
import java.util.*;
import java.util.concurrent.*;

public class OpenGaussProxy {
    static String host, port, user, pass;
    static final Map<String, Connection> connPool = new ConcurrentHashMap<>();

    public static void main(String[] args) throws Exception {
        if (args.length < 4) {
            System.err.println("Usage: java OpenGaussProxy <host> <port> <user> <password>");
            System.exit(1);
        }
        host = args[0]; port = args[1]; user = args[2]; pass = args[3];
        Class.forName("org.postgresql.Driver");
        int httpPort = 18080;
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", httpPort), 0);
        server.createContext("/execute", OpenGaussProxy::handleExecute);
        server.createContext("/health", OpenGaussProxy::handleHealth);
        server.setExecutor(Executors.newFixedThreadPool(4));
        server.start();
        System.out.println("READY");
        System.out.flush();

        // 关闭时清理连接
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            for (Connection c : connPool.values()) {
                try { c.close(); } catch (Exception ignored) {}
            }
        }));
    }

    static synchronized Connection getConnection(String db) throws SQLException {
        Connection conn = connPool.get(db);
        if (conn != null && !conn.isClosed()) {
            try {
                // 验证连接是否仍有效
                if (conn.isValid(2)) return conn;
            } catch (Exception ignored) {}
            try { conn.close(); } catch (Exception ignored) {}
        }
        String url = "jdbc:postgresql://" + host + ":" + port + "/" + db + "?socketTimeout=120&connectTimeout=10";
        conn = DriverManager.getConnection(url, user, pass);
        connPool.put(db, conn);
        return conn;
    }

    static void handleHealth(HttpExchange ex) throws IOException {
        sendJson(ex, 200, "{\"status\":\"ok\"}");
    }

    static void handleExecute(HttpExchange ex) throws IOException {
        if (!"POST".equals(ex.getRequestMethod())) {
            sendJson(ex, 405, "{\"error\":\"POST only\"}");
            return;
        }
        String body = new String(ex.getRequestBody().readAllBytes(), "UTF-8");
        String db = extractField(body, "database");
        String sql = extractField(body, "sql");
        if (sql == null || sql.isEmpty()) {
            sendJson(ex, 400, "{\"error\":\"sql required\"}");
            return;
        }
        if (db == null || db.isEmpty()) db = "firesys_station";

        try {
            Connection conn = getConnection(db);
            try (Statement stmt = conn.createStatement()) {
                stmt.setQueryTimeout(60);
                boolean hasResult = stmt.execute(sql);
                StringBuilder sb = new StringBuilder();
                sb.append("{\"status\":\"ok\",");
                if (hasResult) {
                    ResultSet rs = stmt.getResultSet();
                    ResultSetMetaData meta = rs.getMetaData();
                    int cols = meta.getColumnCount();
                    sb.append("\"columns\":[");
                    for (int i = 1; i <= cols; i++) {
                        if (i > 1) sb.append(",");
                        sb.append(escapeJson(meta.getColumnLabel(i)));
                    }
                    sb.append("],\"rows\":[");
                    boolean first = true;
                    while (rs.next()) {
                        if (!first) sb.append(",");
                        sb.append("{");
                        for (int i = 1; i <= cols; i++) {
                            if (i > 1) sb.append(",");
                            sb.append(escapeJson(meta.getColumnLabel(i))).append(":");
                            String val = rs.getString(i);
                            sb.append(val == null ? "null" : escapeJson(val));
                        }
                        sb.append("}");
                        first = false;
                    }
                    sb.append("]");
                } else {
                    sb.append("\"affected\":").append(stmt.getUpdateCount());
                }
                sb.append("}");
                sendJson(ex, 200, sb.toString());
            }
        } catch (SQLException e) {
            // 连接可能已断开，移除缓存并重试一次
            connPool.remove(db);
            try {
                Connection conn = getConnection(db);
                try (Statement stmt = conn.createStatement()) {
                    stmt.setQueryTimeout(60);
                    boolean hasResult = stmt.execute(sql);
                    StringBuilder sb = new StringBuilder();
                    sb.append("{\"status\":\"ok\",");
                    if (hasResult) {
                        ResultSet rs = stmt.getResultSet();
                        ResultSetMetaData meta = rs.getMetaData();
                        int cols = meta.getColumnCount();
                        sb.append("\"columns\":[");
                        for (int i = 1; i <= cols; i++) {
                            if (i > 1) sb.append(",");
                            sb.append(escapeJson(meta.getColumnLabel(i)));
                        }
                        sb.append("],\"rows\":[");
                        boolean first = true;
                        while (rs.next()) {
                            if (!first) sb.append(",");
                            sb.append("{");
                            for (int i = 1; i <= cols; i++) {
                                if (i > 1) sb.append(",");
                                sb.append(escapeJson(meta.getColumnLabel(i))).append(":");
                                String val = rs.getString(i);
                                sb.append(val == null ? "null" : escapeJson(val));
                            }
                            sb.append("}");
                            first = false;
                        }
                        sb.append("]");
                    } else {
                        sb.append("\"affected\":").append(stmt.getUpdateCount());
                    }
                    sb.append("}");
                    sendJson(ex, 200, sb.toString());
                }
            } catch (Exception retryEx) {
                sendJson(ex, 500, "{\"status\":\"error\",\"error\":" + escapeJson(retryEx.getMessage()) + "}");
            }
        } catch (Exception e) {
            sendJson(ex, 500, "{\"status\":\"error\",\"error\":" + escapeJson(e.getMessage()) + "}");
        }
    }

    static String extractField(String json, String field) {
        String key = "\"" + field + "\":\"";
        int start = json.indexOf(key);
        if (start < 0) return null;
        start += key.length();
        StringBuilder sb = new StringBuilder();
        for (int i = start; i < json.length(); i++) {
            char c = json.charAt(i);
            if (c == '\\' && i + 1 < json.length()) {
                sb.append(json.charAt(++i));
            } else if (c == '"') {
                break;
            } else {
                sb.append(c);
            }
        }
        return sb.toString();
    }

    static String escapeJson(String s) {
        if (s == null) return "null";
        StringBuilder sb = new StringBuilder("\"");
        for (char c : s.toCharArray()) {
            switch (c) {
                case '"': sb.append("\\\""); break;
                case '\\': sb.append("\\\\"); break;
                case '\n': sb.append("\\n"); break;
                case '\r': sb.append("\\r"); break;
                case '\t': sb.append("\\t"); break;
                default: sb.append(c);
            }
        }
        return sb.append("\"").toString();
    }

    static void sendJson(HttpExchange ex, int code, String json) throws IOException {
        byte[] bytes = json.getBytes("UTF-8");
        ex.getResponseHeaders().set("Content-Type", "application/json; charset=UTF-8");
        ex.sendResponseHeaders(code, bytes.length);
        ex.getResponseBody().write(bytes);
        ex.getResponseBody().close();
    }
}
