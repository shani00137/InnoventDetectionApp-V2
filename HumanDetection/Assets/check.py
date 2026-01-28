import paddle

print("CUDA available:", paddle.is_compiled_with_cuda())
print("Device:", paddle.device.get_device())
