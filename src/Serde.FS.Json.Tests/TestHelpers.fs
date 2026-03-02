[<AutoOpen>]
module TestHelpers

open NUnit.Framework

/// Assert equals: actual =! expected
let inline (=!) (actual: 'T) (expected: 'T) =
    Assert.AreEqual(expected, actual)

/// Assert not equals: actual <>! expected
let inline (<>!) (actual: 'T) (expected: 'T) =
    Assert.AreNotEqual(expected, actual)
