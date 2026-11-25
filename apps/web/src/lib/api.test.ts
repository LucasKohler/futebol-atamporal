import { describe, expect, it } from "vitest";
import { problemMessage } from "./api";

describe("problemMessage", () => {
  it("prefers the business rule detail", () => {
    expect(problemMessage({ title: "Regra de negócio", detail: "Transição de Draft para Completed não é permitida." }, "x")).toBe(
      "Transição de Draft para Completed não é permitida.",
    );
  });

  it("names invalid fields from validation problems", () => {
    const body = { title: "One or more validation errors occurred.", errors: { "$.status": ["could not be converted"], body: ["required"] } };
    expect(problemMessage(body, "x")).toBe("Valor inválido em: status, body.");
  });

  it("falls back when the body is not a problem", () => {
    expect(problemMessage(null, "Erro 500")).toBe("Erro 500");
    expect(problemMessage({ title: "Conflito" }, "x")).toBe("Conflito");
  });
});
