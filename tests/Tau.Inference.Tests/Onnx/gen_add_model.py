"""Generate add.onnx, the provider-spike model: y = a + b, float32 [2, 3].

    uv run --no-project --python 3.12 --with onnx==1.19.1 python tests/Tau.Inference.Tests/Onnx/gen_add_model.py
"""
import os

import onnx
from onnx import TensorProto, helper

a = helper.make_tensor_value_info("a", TensorProto.FLOAT, [2, 3])
b = helper.make_tensor_value_info("b", TensorProto.FLOAT, [2, 3])
y = helper.make_tensor_value_info("y", TensorProto.FLOAT, [2, 3])
graph = helper.make_graph([helper.make_node("Add", ["a", "b"], ["y"], name="add")], "tau_add", [a, b], [y])
model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 17)], producer_name="tau")
model.ir_version = 8  # ONNX Runtime 1.24 reads IR <= 11; 8 is the IR version paired with opset 17
onnx.checker.check_model(model, full_check=True)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "add.onnx")
onnx.save(model, out)
print(f"wrote {out} ({os.path.getsize(out)} bytes)")
