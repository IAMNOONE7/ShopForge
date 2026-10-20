import { expect, it } from "vitest";
import { methodVat } from "./methodValidation";
it.each([["0",0],["0,00",0],["21.125",21.125],[" 07,5 ",7.5],["100",100],["0.00000001",0.00000001],["21.10000000000000000000000",21.1],
  ["",null],["-1",null],["100.01",null],["NaN",null],["1e1",null],["1,2.3",null],["21.0000000000000000000001",null]] as const)("reads fallback VAT %s without trimming its meaningful precision", (text, expected) => expect(methodVat(text)).toBe(expected));
