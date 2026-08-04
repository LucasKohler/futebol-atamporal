import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { SessionProvider } from "@/lib/useSession";
import "./globals.css";

const inter = Inter({
  subsets: ["latin"],
  variable: "--font-inter",
  weight: ["400", "600", "700", "800"],
});

export const metadata: Metadata = {
  title: "Futebol Atemporal",
  description: "Administração de partidas recorrentes de futebol casual",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="pt-BR">
      <body className={`${inter.variable} font-sans antialiased`}>
        <SessionProvider>{children}</SessionProvider>
      </body>
    </html>
  );
}
